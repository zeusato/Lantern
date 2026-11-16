using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Lantern.Core;

public sealed class MobileServer : IAsyncDisposable
{
    private readonly PeerNode node; private readonly WorkspaceVault vault;
    public MobileRegistry Registry { get; }
    private readonly CancellationTokenSource life = new();
    private readonly ConcurrentDictionary<long, Task> tasks = new(); private long taskId;
    private readonly ConcurrentDictionary<string, Client> clients = new();
    private readonly ConcurrentDictionary<string, byte> watching = new();
    private readonly SemaphoreSlim slots = new(12, 12), mutations = new(1, 1);
    private readonly object invitationGate = new(); private MobileInvitation? invitation;
    private TcpListener? listener;
    public int Port { get; private set; }
    public string RelayStatus { get; private set; } = "Chưa cấu hình Internet";
    public Func<MobilePairingRequest, Task<bool>>? ApprovePairing { get; set; }
    public event Action? Changed;
    private sealed class Client(CancellationTokenSource stop)
    {
        public CancellationTokenSource Stop { get; } = stop;
        public ConcurrentDictionary<string, byte> Unlocked { get; } = new();
        public Upload? Upload { get; set; }
    }
    private sealed record Upload(string Id, string Conversation, string Path, long Size, string Hash);
    public MobileServer(PeerNode node, WorkspaceVault vault)
    { this.node = node; this.vault = vault; Registry = new MobileRegistry(node.Store.DirectoryPath, vault); }
    private void Track(Task task)
    { var id = Interlocked.Increment(ref taskId); tasks[id] = task; _ = task.ContinueWith(t => { tasks.TryRemove(id, out _); _ = t.Exception; }, TaskScheduler.Default); }
    public Task StartAsync(int port = 45854, bool loopbackOnly = false)
    {
        listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port);
        try { listener.Start(); } catch (SocketException) when (port != 0) { listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, 0); listener.Start(); }
        Port = ((IPEndPoint)listener.LocalEndpoint).Port; Track(Accept()); StartRelay(); return Task.CompletedTask;
    }
    private async Task Accept()
    {
        try
        {
            while (!life.IsCancellationRequested)
            {
                var tcp = await listener!.AcceptTcpClientAsync(life.Token);
                if (tcp.Client.RemoteEndPoint is not IPEndPoint ep || !PeerNode.IsLanAddress(ep.Address) || !slots.Wait(0)) { tcp.Dispose(); continue; }
                Track(RunTcp(tcp));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task RunTcp(TcpClient tcp) { using (tcp) { try { await Serve(tcp.GetStream(), null); } finally { slots.Release(); } } }
    public void ConfigureRelay(RelayConfiguration config)
    {
        if (Registry.Snapshot().Devices.Count > 0) throw new InvalidOperationException("Gỡ các thiết bị trước khi đổi relay.");
        CancelInvitation(); Registry.Configure(config); StartRelay();
    }
    private void StartRelay() { foreach (var device in Registry.Snapshot().Devices) if (device.Route is not null) Watch(device.Route); }
    private void Watch(MobileRoute route) { if (watching.TryAdd(route.Id, 0)) Track(WatchRoute(route)); }
    private bool RouteActive(string id)
    {
        lock (invitationGate) return invitation is { } p && p.Expires > DateTimeOffset.UtcNow && p.Route?.Id == id || Registry.Snapshot().Devices.Any(d => d.Route?.Id == id);
    }
    private async Task WatchRoute(MobileRoute route)
    {
        var config = Registry.Snapshot().Relay;
        using var api = new RelayApi(config); string previous = ""; DateTime registered = DateTime.MinValue;
        try
        {
            while (!life.IsCancellationRequested && RouteActive(route.Id))
            {
                try
                {
                    if (DateTime.UtcNow - registered > TimeSpan.FromHours(12)) { await api.Register(route, life.Token); registered = DateTime.UtcNow; }
                    var state = await api.Call("v1/routes/" + route.Id + "/state", route.HostKey, null, life.Token);
                    RelayStatus = "Internet sẵn sàng"; Changed?.Invoke();
                    var session = state.GetProperty("session").GetString() ?? "";
                    if (session.Length > 0 && session != previous)
                    {
                        previous = session;
                        await using var stream = new HttpRelayStream(api, route, session, true, life.Token);
                        await Serve(stream, route.Id);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { RelayStatus = "Đang thử nối lại relay"; Changed?.Invoke(); }
                await Task.Delay(2500, life.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            watching.TryRemove(route.Id, out _);
            if (!life.IsCancellationRequested) { try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); await api.Call("v1/routes/" + route.Id + "/revoke", route.HostKey, new { }, timeout.Token); } catch (Exception) { } }
        }
    }
    public async Task<string> CreateInvitationAsync()
    {
        CancelInvitation(); var state = Registry.Snapshot();
        var route = state.Relay.Enabled ? new MobileRoute() : null;
        if (route is not null) { using var api = new RelayApi(state.Relay); await api.Register(route, life.Token); }
        var pending = new MobileInvitation(MobileRoute.Secret(), DateTimeOffset.UtcNow.AddMinutes(2), route);
        lock (invitationGate) invitation = pending;
        if (route is not null) Watch(route);
        return JsonSerializer.Serialize(new { v = 1, name = node.Identity.Name, pc = node.Identity.Id, addresses = PeerNode.LocalAddresses(), port = Port, nonce = pending.Nonce, expires = pending.Expires.ToUnixTimeSeconds(), relay = route is null ? null : new { url = state.Relay.Url, token = state.Relay.ServiceToken, id = route.Id, key = route.ClientKey } }, MobileProtocol.Json);
    }
    public void CancelInvitation() { lock (invitationGate) invitation = null; }
    public void LockConversation(string id) { foreach (var client in clients.Values) client.Unlocked.TryRemove(id, out _); }
    public async Task RevokeAsync(string id)
    {
        var device = Registry.Remove(id); if (clients.TryRemove(id, out var client)) client.Stop.Cancel();
        if (device?.Route is { } route && Registry.Snapshot().Relay.Enabled)
        { try { using var api = new RelayApi(Registry.Snapshot().Relay); await api.Call("v1/routes/" + route.Id + "/revoke", route.HostKey, new { }, life.Token); } catch (HttpRequestException) { } }
        Changed?.Invoke();
    }
    private async Task Serve(Stream transport, string? routeId)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
        using var ssl = new SslStream(transport, true, (_, cert, _, _) => cert is not null);
        string deviceId = ""; var client = new Client(stop);
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); helloTimeout.CancelAfter(TimeSpan.FromSeconds(70));
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = node.Identity.Certificate, ClientCertificateRequired = true, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, helloTimeout.Token);
            if (ssl.RemoteCertificate is null) throw new AuthenticationException();
            using var certificate = new X509Certificate2(ssl.RemoteCertificate); deviceId = Identity.PeerId(certificate);
            var hello = await MobileProtocol.ReadAsync(ssl, helloTimeout.Token);
            if (hello.Type != "hello") throw new AuthenticationException();
            var known = Registry.Snapshot().Devices.Find(d => d.Id == deviceId);
            bool freshInvitation;
            lock (invitationGate) freshInvitation = invitation is { } candidate && candidate.Nonce.Length == 43 && candidate.Expires > DateTimeOffset.UtcNow && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate.Nonce), Encoding.UTF8.GetBytes(hello.Nonce));
            // A fresh QR can re-pair the same phone after it forgot its local configuration.
            // An already approved phone may also retry a lost welcome without consuming another QR.
            if (known is null || freshInvitation)
            {
                MobileInvitation pending;
                lock (invitationGate)
                {
                    pending = invitation ?? throw new AuthenticationException();
                    if (pending.Expires <= DateTimeOffset.UtcNow || pending.Nonce.Length != 43 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pending.Nonce), Encoding.UTF8.GetBytes(hello.Nonce)) || routeId is not null && routeId != pending.Route?.Id) throw new AuthenticationException();
                    // Consume the nonce before presenting approval, so it cannot be raced by another connection.
                    invitation = pending with { Nonce = "", Expires = DateTimeOffset.UtcNow.AddSeconds(100) };
                }
                var name = PeerNode.ValidateDisplayName(hello.Name);
                if (ApprovePairing is null || !await ApprovePairing(new(deviceId, name, Identity.VerificationCode(node.Identity.Id, deviceId))).WaitAsync(TimeSpan.FromSeconds(90), stop.Token)) throw new AuthenticationException();
                Registry.Add(new MobileDevice { Id = deviceId, Name = name, Route = pending.Route }); Changed?.Invoke();
            }
            else if (routeId is not null && known.Route?.Id != routeId) throw new AuthenticationException();
            if (clients.TryGetValue(deviceId, out var old)) old.Stop.Cancel(); clients[deviceId] = client;
            await MobileProtocol.WriteAsync(ssl, new MobilePacket { Type = "welcome", Name = node.Identity.Name, Id = node.Identity.Id }, stop.Token);
            while (!stop.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(90));
                var request = await MobileProtocol.ReadAsync(ssl, timeout.Token);
                if (!Registry.Contains(deviceId)) throw new AuthenticationException();
                MobilePacket response;
                try { response = await Handle(client, deviceId, request, stop.Token); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException) { response = new() { Type = "error", Text = ex.Message }; }
                response.Id = request.Id; await MobileProtocol.WriteAsync(ssl, response, timeout.Token);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally { if (client.Upload is { } upload) { try { File.Delete(upload.Path); } catch (IOException) { } } if (clients.TryGetValue(deviceId, out var current) && ReferenceEquals(current, client)) clients.TryRemove(deviceId, out _); Changed?.Invoke(); }
    }
    private bool Locked(Client client, string id) => vault.IsConversationLocked(id) && !client.Unlocked.ContainsKey(id);
    private void ValidateConversation(Client client, string id)
    {
        if (!(node.FindPeer(id) is { Trusted: true, Blocked: false }) && !node.Store.Groups().Any(g => g.Id == id && g.Members.Contains(node.Identity.Id))) throw new ArgumentException("Hội thoại không khả dụng.");
        if (Locked(client, id)) throw new UnauthorizedAccessException("Hội thoại đang khóa.");
    }
    private async Task<MobilePacket> Handle(Client client, string deviceId, MobilePacket request, CancellationToken ct)
    {
        var reply = new MobilePacket { Type = "ok", Conversation = request.Conversation };
        if (request.Type == "list")
        {
            var unread = node.Store.UnreadCounts();
            reply.Conversations = node.Peers.Where(p => p.Trusted && !p.Blocked).Select(p => new MobileConversation(p.Id, p.DisplayName, p.Connected ? p.Presence : "offline", unread.GetValueOrDefault(p.Id), Locked(client, p.Id), false))
                .Concat(node.Store.Groups().Where(g => g.Members.Contains(node.Identity.Id)).Select(g => new MobileConversation(g.Id, g.Name, "group", unread.GetValueOrDefault(g.Id), Locked(client, g.Id), true))).ToList(); return reply;
        }
        if (request.Type == "unlock")
        {
            // Rate limit expensive password derivation per connected device.
            await Task.Delay(1500, ct);
            if (!vault.VerifyConversation(request.Conversation, request.Password)) throw new UnauthorizedAccessException("Mật khẩu chưa đúng.");
            client.Unlocked[request.Conversation] = 0; return reply;
        }
        if (request.Type == "lock") { client.Unlocked.TryRemove(request.Conversation, out _); return reply; }
        ValidateConversation(client, request.Conversation);
        if (request.Type == "upload-start")
        {
            if (request.Size is < 0 or > MobileProtocol.MaxMobileFile || !Guid.TryParseExact(request.Id, "N", out _) || request.Hash.Length != 64 || !request.Hash.All(Uri.IsHexDigit)) throw new ArgumentException("Tệp không hợp lệ hoặc vượt 100 MB.");
            if (client.Upload is { } previous) File.Delete(previous.Path);
            var name = Protocol.SafeFileName(request.FileName);
            var path = Path.Combine(node.Store.MediaPath, "mobile-" + Guid.NewGuid().ToString("N") + "_" + name);
            using (File.Create(path)) { }
            client.Upload = new(request.Id, request.Conversation, path, request.Size, request.Hash); return reply;
        }
        if (request.Type is "upload-chunk" or "upload-finish")
        {
            var upload = client.Upload;
            if (upload is null || upload.Conversation != request.Conversation) throw new ArgumentException("Hãy chọn lại tệp để gửi.");
            if (request.Type == "upload-chunk")
            {
                byte[] bytes; try { bytes = Convert.FromBase64String(request.Data); } catch (FormatException) { throw new ArgumentException("Dữ liệu tệp không hợp lệ."); }
                await using var file = new FileStream(upload.Path, FileMode.Open, FileAccess.Write, FileShare.None, 32768, true);
                if (bytes.Length is < 1 or > 32768 || request.Offset != file.Length || file.Length + bytes.Length > upload.Size) throw new ArgumentException("Sai thứ tự dữ liệu tệp.");
                file.Position = file.Length; await file.WriteAsync(bytes, ct); return reply;
            }
            await using (var file = File.OpenRead(upload.Path))
                if (file.Length != upload.Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(upload.Hash, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Tệp chưa đủ hoặc sai checksum.");
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId + upload.Id)))[..32].ToLowerInvariant();
            await mutations.WaitAsync(ct);
            try { reply.MessageId = (await node.OfferFileAsync(request.Conversation, upload.Path, ct, id, request.FileName)).Id; }
            finally { mutations.Release(); File.Delete(upload.Path); client.Upload = null; }
            return reply;
        }
        if (request.Type == "history")
        {
            reply.Messages = node.Store.Messages(request.Conversation, limit: 50).Select(m => { m.FilePath = ""; m.Error = ""; return m; }).ToList(); return reply;
        }
        if (request.Type == "read") { await node.MarkReadAsync(request.Conversation, request.VisibleIds.ToHashSet()); return reply; }
        if (request.Type == "send")
        {
            if (!Guid.TryParseExact(request.Id, "N", out _)) throw new ArgumentException("Mã gửi không hợp lệ.");
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId + request.Id)))[..32].ToLowerInvariant();
            await mutations.WaitAsync(ct);
            try { reply.MessageId = (await node.SendTextAsync(request.Conversation, request.Text, request.ReplyTo, id)).Id; }
            finally { mutations.Release(); }
            return reply;
        }
        var message = node.Store.Message(request.MessageId);
        if (message is null || message.ConversationId != request.Conversation) throw new ArgumentException("Tin nhắn không còn tồn tại.");
        switch (request.Type)
        {
            case "react": await node.ReactAsync(message.Id, request.Emoji); return reply;
            case "accept": await node.AcceptFileAsync(message.Id); return reply;
            case "download":
                if (request.Offset < 0 || request.Offset > message.FileSize || message.FileSize > MobileProtocol.MaxMobileFile || message.Kind is not ("image" or "video" or "file") || !File.Exists(message.FilePath)) throw new ArgumentException("Tệp chưa sẵn sàng hoặc vượt 100 MB.");
                await using (var file = new FileStream(message.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true))
                { file.Position = request.Offset; var bytes = new byte[32768]; var count = await file.ReadAsync(bytes, ct); reply.Data = Convert.ToBase64String(bytes, 0, count); reply.Offset = request.Offset; reply.Size = message.FileSize; reply.Hash = message.Hash; reply.FileName = message.FileName; }
                return reply;
            default: throw new ArgumentException("Chức năng mobile chưa được hỗ trợ.");
        }
    }
    public async ValueTask DisposeAsync()
    {
        life.Cancel(); listener?.Stop(); foreach (var client in clients.Values) client.Stop.Cancel();
        try { await Task.WhenAll(tasks.Values).WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { }
        life.Dispose();
    }
}
