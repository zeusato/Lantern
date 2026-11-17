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
        public string Selected { get; set; } = "";
        public string Hash { get; set; } = "";
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
    private async Task RunTcp(TcpClient tcp) { using (tcp) { tcp.NoDelay = true; try { await Serve(tcp.GetStream(), null); } finally { slots.Release(); } } }
    public void ConfigureRelay(RelayConfiguration config)
    {
        if (Registry.Snapshot().Devices.Count > 0) throw new InvalidOperationException("Gỡ các thiết bị trước khi đổi relay.");
        CancelInvitation(); Registry.Configure(config); StartRelay();
    }
    private void StartRelay() { foreach (var device in Registry.Snapshot().Devices) if (device.Route is not null) Watch(device.Route); }
    private void Watch(MobileRoute route, bool registered = false) { if (watching.TryAdd(route.Id, 0)) Track(WatchRoute(route, registered)); }
    private bool RouteActive(string id)
    {
        lock (invitationGate) return invitation is { } p && p.Expires > DateTimeOffset.UtcNow && p.Route?.Id == id || Registry.Snapshot().Devices.Any(d => d.Route?.Id == id);
    }
    private async Task WatchRoute(MobileRoute route, bool alreadyRegistered)
    {
        var config = Registry.Snapshot().Relay;
        using var api = new RelayApi(config); string previous = ""; DateTime registered = alreadyRegistered ? DateTime.UtcNow : DateTime.MinValue;
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
                        await Serve(stream, route.Id, route.Transport == "web-v1" ? session : null);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { RelayStatus = "Đang thử nối lại relay"; Changed?.Invoke(); }
                await Task.Delay(route.Transport == "web-v1" ? 250 : 2500, life.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            watching.TryRemove(route.Id, out _);
            if (!life.IsCancellationRequested) { try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); await api.Call("v1/routes/" + route.Id + "/revoke", route.HostKey, new { }, timeout.Token); } catch (Exception) { } }
        }
    }
    public async Task<string> CreateInvitationAsync(bool web = false, bool allowLanFallback = false, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(life.Token, cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        CancelInvitation(); var state = Registry.Snapshot();
        if (web && !state.Relay.Enabled) { Registry.EnsureAutomaticRelay(); state = Registry.Snapshot(); }
        var route = state.Relay.Enabled ? new MobileRoute { Transport = web ? "web-v1" : "tls" } : null;
        if (route is not null)
        {
            try
            {
                using var api = new RelayApi(state.Relay); await api.Register(route, timeout.Token);
                cancellationToken.ThrowIfCancellationRequested(); life.Token.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                // Registration may have committed before its response was lost/cancelled.
                // Release that capability so retries do not consume the active-route quota.
                Track(RevokeUnclaimedRoute(route, state.Relay));
                if (web || !allowLanFallback || life.IsCancellationRequested || cancellationToken.IsCancellationRequested) throw;
                route = null;
            }
        }
        cancellationToken.ThrowIfCancellationRequested(); life.Token.ThrowIfCancellationRequested();
        var pending = new MobileInvitation(MobileRoute.Secret(), DateTimeOffset.UtcNow.AddMinutes(2), route);
        lock (invitationGate) invitation = pending;
        if (route is not null) Watch(route, registered: true);
        // The phone receives only its route capability, never the installation key.
        // Legacy native private-relay configurations retain their service header.
        return JsonSerializer.Serialize(new { v = web ? 2 : 1, name = node.Identity.Name, pc = node.Identity.Id, addresses = web ? [] : PeerNode.LocalAddresses(), port = Port, nonce = pending.Nonce, expires = pending.Expires.ToUnixTimeSeconds(), relay = route is null ? null : new { url = state.Relay.Url, token = web ? "" : state.Relay.ServiceToken, id = route.Id, key = route.ClientKey } }, MobileProtocol.Json);
    }
    private static async Task RevokeUnclaimedRoute(MobileRoute route, RelayConfiguration config)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var api = new RelayApi(config);
            await api.Call("v1/routes/" + route.Id + "/revoke", route.HostKey, new { }, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
    }
    public void CancelInvitation() { lock (invitationGate) invitation = null; }
    public void LockConversation(string id) { foreach (var client in clients.Values) client.Unlocked.TryRemove(id, out _); Changed?.Invoke(); }
    public async Task RevokeAsync(string id)
    {
        var device = Registry.Remove(id); if (clients.TryRemove(id, out var client)) client.Stop.Cancel();
        if (device?.Route is { } route && Registry.Snapshot().Relay.Enabled)
        { try { using var api = new RelayApi(Registry.Snapshot().Relay); await api.Call("v1/routes/" + route.Id + "/revoke", route.HostKey, new { }, life.Token); } catch (HttpRequestException) { } }
        Changed?.Invoke();
    }
    private async Task Serve(Stream transport, string? routeId, string? webSession = null)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(life.Token); timeout.CancelAfter(TimeSpan.FromSeconds(35));
            if (webSession is not null)
            {
                await using var channel = await WebSecureStream.AcceptAsync(transport, node.Identity, routeId!, webSession, timeout.Token);
                await ServeAuthenticated(channel, routeId, channel.RemoteId);
            }
            else
            {
                using var ssl = new SslStream(transport, true, (_, cert, _, _) => cert is not null);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = node.Identity.Certificate, ClientCertificateRequired = true, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, timeout.Token);
                if (ssl.RemoteCertificate is null) throw new AuthenticationException();
                using var certificate = new X509Certificate2(ssl.RemoteCertificate);
                await ServeAuthenticated(ssl, routeId, Identity.PeerId(certificate));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }
    private async Task ServeAuthenticated(Stream ssl, string? routeId, string deviceId)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
        var client = new Client(stop);
        using var writes = new SemaphoreSlim(1, 1);
        using var updates = new SemaphoreSlim(0, 1);
        Task? pushing = null; Action? onChange = null; long serial = 0;
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); helloTimeout.CancelAfter(TimeSpan.FromSeconds(70));
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
            await MobileProtocol.WriteAsync(ssl, new MobilePacket { Type = "welcome", Name = node.Identity.Name, Id = node.Identity.Id, Capabilities = ["sync-v1", "push-v1"] }, stop.Token);
            if (hello.Capabilities?.Contains("push-v1") == true)
            {
                onChange = () => { try { if (updates.CurrentCount == 0) updates.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { } };
                node.Changed += onChange; Changed += onChange;
                pushing = Task.Run(async () =>
                {
                    try
                    {
                        while (!stop.IsCancellationRequested)
                        {
                            await updates.WaitAsync(stop.Token); await Task.Delay(25, stop.Token);
                            await writes.WaitAsync(stop.Token);
                            try { var update = await Handle(client, deviceId, new MobilePacket { Type = "sync", Conversation = client.Selected }, stop.Token); update.Id = ""; update.Serial = ++serial; await MobileProtocol.WriteAsync(ssl, update, stop.Token); }
                            finally { writes.Release(); }
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { stop.Cancel(); }
                });
            }
            while (!stop.IsCancellationRequested)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(90));
                var request = await MobileProtocol.ReadAsync(ssl, timeout.Token);
                if (!Registry.Contains(deviceId)) throw new AuthenticationException();
                await writes.WaitAsync(stop.Token);
                try
                {
                    MobilePacket response;
                    try { response = await Handle(client, deviceId, request, stop.Token); }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException) { response = new() { Type = "error", Text = ex.Message }; }
                    response.Id = request.Id; response.Serial = ++serial; await MobileProtocol.WriteAsync(ssl, response, timeout.Token);
                }
                finally { writes.Release(); }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        finally { if (onChange is not null) { node.Changed -= onChange; Changed -= onChange; } stop.Cancel(); if (pushing is not null) try { await pushing; } catch (Exception) { } if (client.Upload is { } upload) { try { File.Delete(upload.Path); } catch (IOException) { } } if (clients.TryGetValue(deviceId, out var current) && ReferenceEquals(current, client)) clients.TryRemove(deviceId, out _); Changed?.Invoke(); }
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
        if (request.Type is "delete-messages" or "clear-history")
        {
            ValidateConversation(client, request.Conversation);
            if (request.Type == "clear-history") await node.DeleteConversationHistoryAsync(request.Conversation);
            else await node.DeleteMessagesAsync(request.Conversation, request.MessageIds.ToHashSet());
            FillHistory(reply, ""); return reply;
        }
        if (request.Type is "list" or "sync")
        {
            if (request.Type == "sync") client.Selected = request.Conversation;
            var unread = node.Store.UnreadCounts();
            reply.Conversations = node.Peers.Where(p => p.Trusted && !p.Blocked).Select(p => new MobileConversation(p.Id, p.DisplayName, p.Connected ? p.Presence : "offline", unread.GetValueOrDefault(p.Id), Locked(client, p.Id), false, vault.IsConversationLocked(p.Id)))
                .Concat(node.Store.Groups().Where(g => g.Members.Contains(node.Identity.Id)).Select(g => new MobileConversation(g.Id, g.Name, "group", unread.GetValueOrDefault(g.Id), Locked(client, g.Id), true, vault.IsConversationLocked(g.Id)))).ToList();
            if (request.Type == "sync" && request.Conversation.Length > 0)
            {
                reply.Locked = reply.Conversations.Find(c => c.Id == request.Conversation)?.Locked ?? true;
                if (!reply.Locked)
                {
                    ValidateConversation(client, request.Conversation);
                    // Store read state synchronously, but do not wait for LAN receipt delivery.
                    if (request.VisibleIds.Count > 0) Track(node.MarkReadAsync(request.Conversation, request.VisibleIds.ToHashSet()));
                    FillHistory(reply, request.Hash);
                    client.Hash = reply.Hash;
                }
            }
            var policies = node.Store.Policies().ToDictionary(p => p.Id, p => p.DisappearSeconds);
            var versions = node.Store.HistoryVersions();
            reply.Conversations = reply.Conversations.Select(c => c with { DisappearSeconds = policies.GetValueOrDefault(c.Id), HistoryVersion = versions.GetValueOrDefault(c.Id) }).ToList();
            return reply;
        }
        if (request.Type == "unlock")
        {
            // Failed attempts remain throttled; a correct password need not wait artificially.
            if (!vault.VerifyConversation(request.Conversation, request.Password)) { await Task.Delay(1500, ct); throw new UnauthorizedAccessException("Mật khẩu chưa đúng."); }
            client.Unlocked[request.Conversation] = 0;
            ValidateConversation(client, request.Conversation); FillHistory(reply, ""); client.Hash = reply.Hash; return reply;
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
            FillHistory(reply, request.Hash);
            return reply;
        }
        if (request.Type == "read") { await node.MarkReadAsync(request.Conversation, request.VisibleIds.ToHashSet()); return reply; }
        if (request.Type == "send")
        {
            if (!Guid.TryParseExact(request.Id, "N", out _)) throw new ArgumentException("Mã gửi không hợp lệ.");
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId + request.Id)))[..32].ToLowerInvariant();
            await mutations.WaitAsync(ct);
            try { if (node.Store.WasDeleted(id)) { reply.Type = "gone"; reply.MessageId = id; return reply; } var sent = await node.SendTextAsync(request.Conversation, request.Text, request.ReplyTo, id, deliverInBackground: true); reply.MessageId = sent.Id; reply.Messages = MobileProtocol.BoundedHistory([sent]); }
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
    private void FillHistory(MobilePacket reply, string previousHash)
    {
        reply.Messages = MobileProtocol.BoundedHistory(node.Store.Messages(reply.Conversation, limit: 50));
        reply.Hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(reply.Messages, MobileProtocol.Json)));
        if (reply.Hash == previousHash) { reply.Type = "unchanged"; reply.Messages = []; }
    }
    public async ValueTask DisposeAsync()
    {
        life.Cancel(); listener?.Stop(); foreach (var client in clients.Values) client.Stop.Cancel();
        try { await Task.WhenAll(tasks.Values).WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { }
        life.Dispose();
    }
}
