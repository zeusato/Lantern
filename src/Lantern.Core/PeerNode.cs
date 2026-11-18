using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Lantern.Core;

public sealed partial class PeerNode : IAsyncDisposable
{
    public const int DiscoveryPort = 45852;
    public const string MulticastAddress = "239.255.42.99";
    public Identity Identity { get; }
    public LocalStore Store { get; }
    public int Port { get; private set; }
    public string Presence { get; private set; } = "online";
    public async Task SetPresenceAsync(string presence)
    {
        if (presence is not ("online" or "idle" or "dnd")) throw new ArgumentException("Trạng thái không hợp lệ.");
        if (Presence == presence) return;
        Presence = presence; Changed?.Invoke();
        foreach (var peer in peers.Values.Where(p => p.Trusted && !p.Blocked)) await SendTo(peer.Id, new WireMessage { Type = "presence", Data = Presence });
    }
    public event Action? Changed;
    public event Action<string>? Notice;
    public event Action<PairRequest>? PairingRequested;
    public event Action<TransferUpdate>? TransferProgress;
    public event Action<ChatMessage>? MessageReceived;
    public static readonly IReadOnlyList<string> ReactionEmoji = Array.AsReadOnly(new[] { "👍", "❤️", "😂", "😮", "😢", "😡", "🎉", "🙏" });
    private readonly ConcurrentDictionary<string, Peer> peers = new();
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    private readonly ConcurrentDictionary<string, byte> connecting = new();
    private readonly ConcurrentDictionary<string, byte> receiving = new();
    private readonly SemaphoreSlim transferStarts = new(1, 1);
    private readonly SemaphoreSlim automaticImages = new(1, 1);
    public bool AutoReceiveImages { get; set; }
    private readonly ConcurrentDictionary<string, byte> sending = new();
    private readonly ConcurrentDictionary<string, byte> replaying = new();
    private readonly ConcurrentDictionary<long, Task> tasks = new();
    private readonly ConcurrentDictionary<string, ContactSearch> contactSearches = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastLookup = new();
    private sealed class ContactSearch
    {
        public ConcurrentDictionary<string, ContactCard> Results { get; } = new();
        public ConcurrentDictionary<string, byte> Remaining { get; } = new();
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public async Task<IReadOnlyList<ContactCard>> FindContactsAsync(string name, CancellationToken ct = default)
    {
        name = ValidateDisplayName(name); if (name.Length < 2) throw new ArgumentException("Nhập ít nhất 2 ký tự của tên hiển thị.");
        var search = new ContactSearch(); var requestId = Guid.NewGuid().ToString("N");
        foreach (var card in MatchingContacts(name)) search.Results[card.Id] = card;
        var sources = Peers.Where(p => p.Trusted && !p.Blocked && p.Connected).ToArray();
        foreach (var peer in sources) search.Remaining[peer.Id] = 0;
        contactSearches[requestId] = search;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        try
        {
            foreach (var peer in sources) await SendTo(peer.Id, new WireMessage { Type = "lookup", Id = requestId, Name = name });
            if (sources.Length > 0) { try { await search.Done.Task.WaitAsync(TimeSpan.FromSeconds(4), cancel.Token); } catch (TimeoutException) { } }
            return search.Results.Values.Where(c => c.Id != Identity.Id && (!peers.TryGetValue(c.Id, out var known) || !known.Blocked)).OrderBy(c => c.Name).Take(100).ToArray();
        }
        finally { contactSearches.TryRemove(requestId, out _); }
    }
    private IEnumerable<ContactCard> MatchingContacts(string name) => Peers.Where(p => p.Trusted && !p.Blocked && p.Port > 0 && p.Name.Contains(name, StringComparison.CurrentCultureIgnoreCase)).Take(20).Select(p => new ContactCard { Id = p.Id, Name = p.Name, Address = p.Address, Port = p.Port });
    private long taskId;
    private readonly SemaphoreSlim handshakes = new(12, 12);
    private readonly CancellationTokenSource lifetime = new();
    private TcpListener? listener;
    private UdpClient? discovery;
    private Task? acceptTask, discoverTask, announceTask, expiryTask;
    public PeerNode(Identity identity, LocalStore store)
    {
        Identity = identity; Store = store;
        foreach (var p in store.Peers()) { p.Connected = false; if (IPAddress.TryParse(p.Address, out var ip) && ip.IsIPv4MappedToIPv6) p.Address = ip.MapToIPv4().ToString(); peers[p.Id] = p; }
    }
    private void Track(Task task)
    {
        var id = Interlocked.Increment(ref taskId); tasks[id] = task;
        _ = task.ContinueWith(completed => { tasks.TryRemove(id, out _); _ = completed.Exception; }, TaskScheduler.Default);
    }
    public IReadOnlyList<Peer> Peers => peers.Values.OrderByDescending(p => p.Connected).ThenBy(p => p.DisplayName).ToArray();
    public static string ValidateDisplayName(string name, bool allowEmpty = false)
    {
        name = name.Trim();
        if ((!allowEmpty && name.Length == 0) || name.Length > 80 || name.Any(char.IsControl)) throw new ArgumentException("Tên phải có từ 1 đến 80 ký tự, không chứa ký tự điều khiển.");
        return name;
    }
    public void SetPeerAlias(string peerId, string alias)
    {
        alias = ValidateDisplayName(alias, true);
        if (!peers.TryGetValue(peerId, out var peer)) throw new ArgumentException("Không tìm thấy người dùng.");
        lock (peer) { peer.LocalAlias = alias; Store.SavePeer(peer); }
        Changed?.Invoke();
    }
    public async Task UpdateDisplayNameAsync(string name)
    {
        Identity.Name = ValidateDisplayName(name); Changed?.Invoke();
        foreach (var peer in peers.Values.Where(p => p.Trusted && !p.Blocked))
            await SendTo(peer.Id, new WireMessage { Type = "profile", Name = Identity.Name, Data = Store.Avatar });
        await AnnounceAsync();
    }
    public Peer? FindPeer(string id) => peers.GetValueOrDefault(id);
    public bool IsConnected(string id) => sessions.ContainsKey(id);
    public static bool IsLanAddress(IPAddress ip) => IPAddress.IsLoopback(ip) || ip.AddressFamily == AddressFamily.InterNetwork && (ip.GetAddressBytes()[0] == 10 || ip.GetAddressBytes()[0] == 192 && ip.GetAddressBytes()[1] == 168 || ip.GetAddressBytes()[0] == 172 && ip.GetAddressBytes()[1] is >= 16 and <= 31 || ip.GetAddressBytes()[0] == 169 && ip.GetAddressBytes()[1] == 254);
    public static string[] LocalAddresses() => LanDiscovery.Interfaces().Select(n => n.Address.ToString()).Distinct().ToArray();

    public Task StartAsync(int port = 45853, bool enableDiscovery = true, bool loopbackOnly = false)
    {
        listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port);
        try { listener.Start(32); }
        catch (SocketException) when (port != 0) { listener = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, 0); listener.Start(32); }
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptTask = AcceptLoop();
        discoveryEnabled = enableDiscovery && !loopbackOnly;
        if (discoveryEnabled)
        {
            try
            {
                discovery = new UdpClient(AddressFamily.InterNetwork);
                discovery.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                discovery.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                discovery.EnableBroadcast = true; discovery.MulticastLoopback = true;
                discoverTask = DiscoveryLoop();
            }
            catch (SocketException ex) { discovery?.Dispose(); discovery = null; Notice?.Invoke("Tự tìm máy qua UDP chưa hoạt động. Bấm Tìm máy để dò trực tiếp. " + ex.SocketErrorCode); }
        }
        announceTask = MaintenanceLoop(discoveryEnabled);
        expiryTask = ExpiryLoop();
        Changed?.Invoke(); return Task.CompletedTask;
    }
    private async Task AcceptLoop()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener!.AcceptTcpClientAsync(lifetime.Token);
                if (client.Client.RemoteEndPoint is not IPEndPoint ep || !IsLanAddress(ep.Address) || !handshakes.Wait(0)) { client.Dispose(); continue; }
                Track(Establish(client, null, false));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    public async Task ConnectAsync(string address, int port = 45853, string? expectedId = null)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || !IsLanAddress(ip)) throw new ArgumentException("Nhập địa chỉ IPv4 trong LAN, ví dụ 192.168.1.25.");
        if (port is < 1 or > 65535) throw new ArgumentException("Cổng phải từ 1 đến 65535.");
        var endpoint = address + ":" + port;
        if (!connecting.TryAdd(endpoint, 0)) return;
        try
        {
            if (!await handshakes.WaitAsync(0, lifetime.Token)) return;
            var client = new TcpClient();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await client.ConnectAsync(ip, port, timeout.Token);
            }
            catch { client.Dispose(); handshakes.Release(); throw; }
            await Establish(client, expectedId, true);
        }
        finally { connecting.TryRemove(endpoint, out _); }
    }
    private async Task Establish(TcpClient client, string? expectedId, bool outbound, bool quiet = false, CancellationToken ct = default)
    {
        Session? session = null;
        var released = false;
        try
        {
            if (sessions.Count >= 64) throw new IOException("Đã đạt giới hạn kết nối.");
            client.NoDelay = true;
            string certificateId = "";
            var ssl = new SslStream(client.GetStream(), false, (_, cert, _, _) =>
            {
                if (cert is null) return false;
                using var remote = new X509Certificate2(cert);
                certificateId = Identity.PeerId(remote);
                return certificateId != Identity.Id && (expectedId is null || certificateId == expectedId) && !(peers.TryGetValue(certificateId, out var p) && p.Blocked);
            });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, ct); timeout.CancelAfter(TimeSpan.FromSeconds(quiet ? 3 : 10));
            if (outbound)
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "lantern.local", ClientCertificates = new X509CertificateCollection { Identity.Certificate }, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, timeout.Token);
            else
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Identity.Certificate, ClientCertificateRequired = true, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, timeout.Token);
            await Protocol.WriteAsync(ssl, new WireMessage { Type = "hello", Version = 5, Id = Identity.Id, Name = Identity.Name, Port = Port, Data = Presence }, timeout.Token);
            var hello = await Protocol.ReadAsync(ssl, timeout.Token);
            if (hello.Type != "hello" || hello.Id != certificateId || hello.Name.Length is < 1 or > 80 || string.IsNullOrWhiteSpace(hello.Name) || hello.Name.Any(char.IsControl) || hello.Port is < 1 or > 65535) throw new AuthenticationException("Danh tính không khớp khóa của kết nối.");
            session = new Session(certificateId, ssl, client, outbound);
            // Simultaneous connections converge on the socket initiated by the lower identity.
            if (!sessions.TryAdd(certificateId, session))
            {
                var existing = sessions.GetValueOrDefault(certificateId);
                var preferOutbound = string.CompareOrdinal(Identity.Id, certificateId) < 0;
                if (existing is null || existing.Outbound == preferOutbound || outbound != preferOutbound) { session.Dispose(); return; }
                if (!sessions.TryUpdate(certificateId, session, existing)) { session.Dispose(); return; }
                existing.Dispose();
            }
            var remoteAddress = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
            var address = (remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress).ToString();
            var peer = peers.GetOrAdd(certificateId, id => new Peer { Id = id });
            lock (peer) { peer.Name = hello.Name; }
            peer.Presence = hello.Data is "idle" or "dnd" ? hello.Data : "online";
            peer.ProtocolVersion = hello.Version;
            peer.Address = address; peer.Port = hello.Port; peer.Connected = true; peer.Seen = DateTimeOffset.UtcNow;
            Store.SavePeer(peer); Changed?.Invoke();
            handshakes.Release(); released = true;
            if (!peer.Trusted) PairingRequested?.Invoke(new PairRequest(peer.Id, peer.Name, peer.Address, Identity.VerificationCode(Identity.Id, peer.Id)));
            else await session.SendAsync(new WireMessage { Type = "ready" }, lifetime.Token);
            Track(ReadLoop(session));
            session = null; // ReadLoop owns the lifetime after successful handshake.
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
        {
            session?.Dispose(); client.Dispose();
            if (!quiet && !lifetime.IsCancellationRequested) Notice?.Invoke("Chưa kết nối được: " + ex.Message + (ex.InnerException is null ? "" : " " + ex.InnerException.Message));
        }
        finally { if (!released) handshakes.Release(); }
    }
    private async Task ReadLoop(Session session)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); idle.CancelAfter(TimeSpan.FromSeconds(45));
                var msg = await Protocol.ReadAsync(session.Stream, idle.Token);
                if (!peers.TryGetValue(session.PeerId, out var peer) || peer.Blocked) break;
                peer.Seen = DateTimeOffset.UtcNow;
                if (msg.Type == "ping") continue;
                if (!peer.Trusted) continue;
                await Handle(session, msg);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException or CryptographicException or ArgumentException or UnauthorizedAccessException or FormatException)
        { if (!lifetime.IsCancellationRequested && ex is not EndOfStreamException) Notice?.Invoke("Kết nối tạm ngắt; tin đang chờ sẽ được thử lại."); }
        finally
        {
            if (sessions.TryGetValue(session.PeerId, out var active) && ReferenceEquals(active, session))
            {
                sessions.TryRemove(session.PeerId, out _);
                if (peers.TryGetValue(session.PeerId, out var peer)) { peer.Connected = false; Store.SavePeer(peer); }
                foreach (var id in receiving.Keys) if (Store.Message(id)?.SenderId == session.PeerId) receiving.TryRemove(id, out _);
            }
            session.Dispose(); Changed?.Invoke();
        }
    }
    public async Task TrustAsync(string peerId)
    {
        if (!peers.TryGetValue(peerId, out var peer) || !sessions.TryGetValue(peerId, out var session)) throw new InvalidOperationException("Máy cần đang kết nối để xác minh.");
        peer.Trusted = true; peer.Blocked = false; Store.SavePeer(peer);
        await session.SendAsync(new WireMessage { Type = "ready" }, lifetime.Token);
        await Replay(peerId); Changed?.Invoke();
    }
    public void Block(string peerId)
    {
        if (!peers.TryGetValue(peerId, out var peer)) return;
        peer.Blocked = true; peer.Trusted = false; Store.SavePeer(peer);
        if (sessions.TryGetValue(peerId, out var session)) session.Dispose();
        Changed?.Invoke();
    }
    public void Unblock(string peerId)
    {
        if (!peers.TryGetValue(peerId, out var peer)) return;
        peer.Blocked = false; peer.Trusted = false; Store.SavePeer(peer); Changed?.Invoke();
    }
    private async Task SendTo(string peerId, WireMessage message)
    {
        if (sessions.TryGetValue(peerId, out var session))
        {
            try { await session.SendAsync(message, lifetime.Token); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { session.Dispose(); }
        }
    }
    private async Task Replay(string peerId, bool includeReceipts = true)
    {
        if (!replaying.TryAdd(peerId, 0)) return;
        try
        {
            if (!peers.TryGetValue(peerId, out var recipient) || !recipient.Trusted || recipient.Blocked) return;
            await ReplayGroupActions(peerId);
            foreach (var rules in Store.Restrictions().Where(p => p.Id == peerId && p.Actor == Identity.Id)) await SendRestrictions(peerId, rules);
            if (includeReceipts) await SendTo(peerId, new WireMessage { Type = "profile", Name = Identity.Name, Data = Store.Avatar });
            await SendTo(peerId, new WireMessage { Type = "presence", Data = Presence });
            foreach (var group in Store.Groups().Where(g => g.OwnerId == Identity.Id && g.Members.Contains(peerId))) await SendTo(peerId, new WireMessage { Type = "group", Group = group });
            foreach (var policy in Store.Policies().Where(p => p.Actor == Identity.Id && (p.Id == peerId || Store.Groups().Any(g => g.Id == p.Id && g.Members.Contains(peerId))))) await SendPolicy(peerId, policy);
            foreach (var group in Store.Groups().Where(g => g.Members.Contains(peerId))) PrepareGroupHistory(group);
            foreach (var msg in Store.Pending(peerId).Where(m => m.SenderId == Identity.Id && ConversationHasPeer(m, peerId) && (!m.ExpiresAt.HasValue || m.ExpiresAt > DateTimeOffset.UtcNow) && (m.DisappearAfterSeconds == 0 || peers[peerId].ProtocolVersion >= 2))) await SendTo(peerId, new WireMessage { Type = msg.Kind == "text" ? "message" : msg.Kind == "contact" ? "contact" : "offer", Message = ForRecipient(msg, peerId) });
            // Replay only this machine's reaction, including removals. Revision numbers reject stale updates.
            foreach (var msg in Store.Messages(limit: int.MaxValue).Where(m => m.Reactions.ContainsKey(Identity.Id) && ConversationHasPeer(m, peerId)))
                await SendTo(peerId, new WireMessage { Type = "reaction", Id = msg.Id, Data = msg.Reactions[Identity.Id], Offset = msg.ReactionVersions.GetValueOrDefault(Identity.Id) });
            foreach (var msg in Store.Messages(limit: int.MaxValue).Concat(Store.ExpiredMessages()).Where(m => m.ExpiresAt.HasValue && ConversationHasPeer(m, peerId)))
                await SendTo(peerId, new WireMessage { Type = "expiry-start", Id = msg.Id, Offset = msg.ExpiresAt!.Value.ToUnixTimeMilliseconds() });
            if (includeReceipts) foreach (var msg in Store.Messages(limit: 500).Where(m => !m.Outgoing && m.SenderId == peerId && m.Status == "read")) await SendTo(peerId, new WireMessage { Type = "read", Id = msg.Id });
        }
        finally { replaying.TryRemove(peerId, out _); }
    }
    private static ChatMessage ForWire(ChatMessage value)
    {
        var msg = Codec.Decode<ChatMessage>(Codec.Encode(value)); msg.FilePath = ""; msg.ExpiresAt = null; msg.Delivery.Clear(); msg.Reactions.Clear(); msg.ReactionVersions.Clear(); msg.Error = ""; msg.Status = ""; msg.Transferred = 0; return msg;
    }
    private List<string> Recipients(string conversationId)
    {
        var group = Store.Groups().Find(g => g.Id == conversationId);
        if (group is not null && !group.Members.Contains(Identity.Id)) throw new InvalidOperationException("Bạn đã rời nhóm; lịch sử chỉ còn để xem.");
        var recipients = group is null ? new List<string> { conversationId } : group.Members.Where(id => id != Identity.Id).ToList();
        if (recipients.Count == 0 || recipients.Any(id => !peers.TryGetValue(id, out var p) || !p.Trusted || p.Blocked)) throw new InvalidOperationException("Cần ghép đôi với tất cả người nhận trước khi gửi.");
        return recipients;
    }
    private int DisappearSeconds(string conversationId, IEnumerable<string> recipients)
    {
        var seconds = Store.Policies().Find(p => p.Id == conversationId)?.DisappearSeconds ?? 0;
        if (seconds > 0 && recipients.Any(id => peers[id].ProtocolVersion < 2)) throw new InvalidOperationException("Tin tự hủy yêu cầu tất cả thành viên dùng Lantern 1.1 trở lên.");
        return seconds;
    }
    public async Task<ChatMessage> SendTextAsync(string conversationId, string text, string replyToId = "", string? stableId = null, bool deliverInBackground = false, bool forwarded = false)
    {
        if (stableId is not null)
        {
            if (!Protocol.ValidId(stableId)) throw new ArgumentException("Mã tin nhắn không hợp lệ.");
            if (Store.WasDeleted(stableId)) throw new InvalidOperationException("Tin này đã bị xóa hoặc tự hủy; không gửi lại.");
            var existing = Store.Message(stableId);
            if (existing is not null) { if (!existing.Outgoing || existing.ConversationId != conversationId) throw new ArgumentException("Mã tin nhắn trùng."); return existing; }
        }
        text = text.Trim(); if (text.Length is < 1 or > 16000) throw new ArgumentException("Tin nhắn cần từ 1 đến 16.000 ký tự.");
        var recipients = Recipients(conversationId);
        if (replyToId.Length > 0 && Store.Message(replyToId)?.ConversationId != conversationId) throw new ArgumentException("Tin được trích dẫn không thuộc hội thoại này.");
        var msg = new ChatMessage { ConversationId = conversationId, SenderId = Identity.Id, SenderName = Identity.Name, Text = text, Outgoing = true, GroupId = Store.Groups().Any(g => g.Id == conversationId) ? conversationId : "", Delivery = recipients.ToDictionary(id => id, _ => "queued") };
        msg.ReplyToId = replyToId;
        msg.GroupRevision = GroupRevision(conversationId);
        msg.Forwarded = forwarded;
        if (stableId is not null) msg.Id = stableId;
        msg.DisappearAfterSeconds = DisappearSeconds(conversationId, recipients);
        Store.SaveMessage(msg); Changed?.Invoke();
        async Task Deliver() { foreach (var peerId in recipients) await SendTo(peerId, new WireMessage { Type = "message", Message = ForWire(msg) }); }
        if (deliverInBackground) Track(Deliver()); else await Deliver();
        return msg;
    }
    private bool ConversationHasPeer(ChatMessage message, string peerId) => message.GroupId.Length == 0
        ? message.ConversationId == peerId
        : Store.Groups().Any(g => g.Id == message.GroupId && GroupMembership.CanRead(g, peerId, message) && GroupMembership.CanRead(g, Identity.Id, message));
    public async Task SetDisappearTimerAsync(string conversationId, int seconds, bool deliverInBackground = false)
    {
        if (seconds != 0 && seconds is < 10 or > 604800) throw new ArgumentException("Thời gian tự hủy phải từ 10 giây đến 7 ngày.");
        var recipients = Recipients(conversationId);
        var policy = new ConversationPolicy { Id = conversationId, Actor = Identity.Id, DisappearSeconds = seconds, Revision = checked((Store.Policies().Find(p => p.Id == conversationId)?.Revision ?? 0) + 1) };
        if (seconds > 0 && recipients.Any(id => peers[id].ProtocolVersion < 2)) throw new InvalidOperationException("Tin tự hủy yêu cầu tất cả thành viên dùng Lantern 1.1 trở lên.");
        Store.SavePolicy(policy); Changed?.Invoke();
        async Task Deliver() { foreach (var peer in recipients) await SendPolicy(peer, policy); }
        if (deliverInBackground) Track(Deliver()); else await Deliver();
    }
    private Task SendPolicy(string peerId, ConversationPolicy policy)
    {
        var wire = new ConversationPolicy { Id = policy.Id == peerId ? "" : policy.Id, Actor = policy.Actor, Revision = policy.Revision, DisappearSeconds = policy.DisappearSeconds };
        return SendTo(peerId, new WireMessage { Type = "expiry-policy", Data = Codec.Encode(wire) });
    }
    public async Task ReactAsync(string messageId, string emoji)
    {
        if (emoji.Length > 0 && !ReactionEmoji.Contains(emoji)) throw new ArgumentException("Biểu cảm không được hỗ trợ.");
        var message = Store.Message(messageId) ?? throw new ArgumentException("Tin nhắn không còn tồn tại.");
        var recipients = Recipients(message.ConversationId);
        long revision = 0;
        Store.UpdateMessage(messageId, current => { revision = checked(current.ReactionVersions.GetValueOrDefault(Identity.Id) + 1); current.Reactions[Identity.Id] = emoji; current.ReactionVersions[Identity.Id] = revision; });
        Changed?.Invoke();
        foreach (var peer in recipients.Where(id => ConversationHasPeer(message, id))) await SendTo(peer, new WireMessage { Type = "reaction", Id = messageId, Data = emoji, Offset = revision });
    }
    public async Task<ChatMessage> ShareContactAsync(string conversationId, string contactId)
    {
        var contact = FindPeer(contactId);
        if (contact is not { Trusted: true, Blocked: false }) throw new ArgumentException("Chỉ chia sẻ người đã ghép đôi.");
        var recipients = Recipients(conversationId);
        var card = new ContactCard { Id = contact.Id, Name = contact.Name, Address = contact.Address, Port = contact.Port }; ValidateContact(card);
        var message = new ChatMessage { ConversationId = conversationId, SenderId = Identity.Id, SenderName = Identity.Name, Outgoing = true, Kind = "contact", Contact = card, GroupId = Store.Groups().Any(g => g.Id == conversationId) ? conversationId : "", Delivery = recipients.ToDictionary(id => id, _ => "queued"), DisappearAfterSeconds = DisappearSeconds(conversationId, recipients) };
        message.GroupRevision = GroupRevision(conversationId);
        Store.SaveMessage(message); Changed?.Invoke();
        foreach (var peer in recipients) await SendTo(peer, new WireMessage { Type = "contact", Message = ForWire(message) });
        return message;
    }
    public static void ValidateContact(ContactCard card)
    {
        if (card is null || card.Id is null || card.Name is null || card.Address is null || card.Id.Length != 64 || !card.Id.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(card.Name) || card.Name.Length > 80 || card.Name.Any(char.IsControl) || !IPAddress.TryParse(card.Address, out var address) || address.AddressFamily != AddressFamily.InterNetwork || !IsLanAddress(address) || card.Port is < 1 or > 65535) throw new InvalidDataException("Liên hệ được chia sẻ không hợp lệ.");
    }
    public async Task<ChatGroup> CreateGroupAsync(string name, IEnumerable<string> memberIds, string avatar = "")
    {
        var members = memberIds.Append(Identity.Id).Distinct().ToList();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || members.Count is < 3 or > 32) throw new ArgumentException("Nhóm cần tên và từ 3 đến 32 thành viên, kể cả người tạo.");
        if (members.Any(id => id != Identity.Id && (!peers.TryGetValue(id, out var p) || !p.Trusted || p.Blocked))) throw new InvalidOperationException("Chỉ thêm người đã ghép đôi.");
        var group = new ChatGroup { Name = name.Trim(), Avatar = avatar, OwnerId = Identity.Id, Members = members }; GroupMembership.Validate(group); Store.SaveGroup(group);
        foreach (var id in members.Where(id => id != Identity.Id)) await SendTo(id, new WireMessage { Type = "group", Group = group });
        Changed?.Invoke(); return group;
    }
    public async Task<ChatMessage> OfferFileAsync(string conversationId, string source, CancellationToken ct = default, string? stableId = null, string? displayFileName = null, bool forwarded = false, string? forwardSource = null)
    {
        if (stableId is not null)
        {
            if (!Protocol.ValidId(stableId)) throw new ArgumentException("Mã tin nhắn không hợp lệ.");
            var existing = Store.Message(stableId);
            if (existing is not null) { if (!existing.Outgoing || existing.ConversationId != conversationId) throw new ArgumentException("Mã tin nhắn trùng."); return existing; }
        }
        var recipients = Recipients(conversationId); var file = new FileInfo(source);
        if (!file.Exists || file.Length > Protocol.MaxFileSize) throw new ArgumentException("Tệp không tồn tại hoặc vượt quá 10 GB.");
        var msg = new ChatMessage { ConversationId = conversationId, SenderId = Identity.Id, SenderName = Identity.Name, Outgoing = true, Kind = "file", FileName = Protocol.SafeFileName(file.Name), FileSize = file.Length, GroupId = Store.Groups().Any(g => g.Id == conversationId) ? conversationId : "", Delivery = recipients.ToDictionary(id => id, _ => "offered"), Status = "offered" };
        msg.GroupRevision = GroupRevision(conversationId);
        msg.DisappearAfterSeconds = DisappearSeconds(conversationId, recipients);
        msg.Forwarded = forwarded;
        if (stableId is not null) msg.Id = stableId;
        if (displayFileName is not null) msg.FileName = Protocol.SafeFileName(displayFileName);
        var ext = Path.GetExtension(msg.FileName).ToLowerInvariant();
        if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" }.Contains(ext)) msg.Kind = "image";
        if (new[] { ".mp4", ".m4v", ".wmv", ".avi", ".mov" }.Contains(ext)) msg.Kind = "video";
        msg.FilePath = Path.Combine(Store.MediaPath, msg.Id + "_" + msg.FileName);
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, Protocol.ChunkSize, true))
            await using (var output = new FileStream(msg.FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, Protocol.ChunkSize, true)) await input.CopyToAsync(output, ct);
            await using (var input = File.OpenRead(msg.FilePath)) msg.Hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
            if (forwardSource is not null)
            {
                var original = Store.Message(forwardSource) ?? throw new InvalidOperationException("Nội dung gốc vừa bị xóa hoặc hết hạn.");
                if (original.ExpiresAt <= DateTimeOffset.UtcNow || !ConversationRules(original.ConversationId).ForwardAllowed) throw new InvalidOperationException("Nội dung không còn cho phép chuyển tiếp.");
            }
            // Membership can change while a large attachment is being copied.
            recipients = Recipients(conversationId); msg.GroupRevision = GroupRevision(conversationId);
            msg.Delivery = recipients.ToDictionary(id => id, _ => "offered");
            Store.SaveMessage(msg);
        }
        catch { try { File.Delete(msg.FilePath); } catch (IOException) { } throw; }
        Changed?.Invoke();
        foreach (var id in recipients) await SendTo(id, new WireMessage { Type = "offer", Message = ForWire(msg) });
        return msg;
    }
    public async Task AcceptFileAsync(string messageId)
    {
        await transferStarts.WaitAsync(lifetime.Token);
        try
        {
            var msg = Store.Message(messageId) ?? throw new InvalidOperationException("Không tìm thấy tệp.");
            if (msg.Outgoing || msg.Kind == "text" || msg.Status == "received") return;
            if (receiving.ContainsKey(messageId)) return;
            if (!IsConnected(msg.SenderId)) throw new InvalidOperationException("Người gửi cần đang online để tải tệp.");
            if (receiving.Count >= 3 && !receiving.ContainsKey(messageId)) throw new InvalidOperationException("Đang tải 3 tệp. Hãy chờ một tệp hoàn tất.");
            var part = msg.FilePath + ".part";
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset > msg.FileSize) { File.Delete(part); offset = 0; }
            var drive = new DriveInfo(Path.GetPathRoot(Store.MediaPath)!);
            if (drive.AvailableFreeSpace < msg.FileSize - offset + 50 * 1024 * 1024) throw new IOException("Không đủ dung lượng trống để nhận tệp.");
            using (var f = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write)) { }
            receiving[messageId] = 0; Store.UpdateMessage(messageId, current => { current.Status = "receiving"; current.Transferred = offset; current.Error = ""; }); Changed?.Invoke();
            await SendTo(msg.SenderId, new WireMessage { Type = "accept", Id = messageId, Offset = offset });
        }
        finally { transferStarts.Release(); }
    }
    private async Task ReceiveImagesAsync()
    {
        if (!AutoReceiveImages || lifetime.IsCancellationRequested || !await automaticImages.WaitAsync(0)) return;
        try
        {
            var pending = Store.Messages(limit: int.MaxValue).Where(m => !m.Outgoing && m.Kind == "image" && m.Status is "offered" or "receiving");
            foreach (var image in pending)
            {
                if (lifetime.IsCancellationRequested || receiving.Count >= 3) break;
                if (receiving.ContainsKey(image.Id) || !peers.TryGetValue(image.SenderId, out var peer) || !peer.Trusted || peer.Blocked || !IsConnected(peer.Id)) continue;
                try { await AcceptFileAsync(image.Id); }
                catch (InvalidOperationException) { /* A manual transfer or disconnect may win the slot; retry later. */ }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Store.UpdateMessage(image.Id, current => { current.Status = "error"; current.Error = ex.Message; }); Changed?.Invoke(); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { automaticImages.Release(); }
    }
    public async Task DeclineFileAsync(string messageId)
    {
        var msg = Store.Message(messageId); if (msg is null || msg.Outgoing) return;
        receiving.TryRemove(messageId, out _); Store.UpdateMessage(messageId, current => current.Status = "declined");
        await SendTo(msg.SenderId, new WireMessage { Type = "decline", Id = msg.Id }); Changed?.Invoke();
    }
    public async Task MarkReadAsync(string conversationId, IReadOnlySet<string>? visibleMessages = null)
    {
        var unread = Store.MarkConversationRead(conversationId, visibleMessages);
        if (unread.Count > 0) Changed?.Invoke();
        foreach (var msg in unread.Where(m => m.Kind is "text" or "contact"))
            await SendTo(msg.SenderId, new WireMessage { Type = "read", Id = msg.Id });
        foreach (var msg in unread.Where(m => m.ExpiresAt.HasValue))
            foreach (var peer in Peers.Where(p => p.Trusted && !p.Blocked && ConversationHasPeer(msg, p.Id))) await SendTo(peer.Id, new WireMessage { Type = "expiry-start", Id = msg.Id, Offset = msg.ExpiresAt!.Value.ToUnixTimeMilliseconds() });
    }
    private async Task Handle(Session session, WireMessage wire)
    {
        var peerId = session.PeerId;
        if (wire.Type == "group-action") { await HandleGroupAction(wire.Data); return; }
        if (wire.Type == "conversation-rules")
        {
            var rules = Codec.Decode<ConversationRestrictions>(wire.Data);
            if (rules.Id != "" || rules.Actor != peerId || rules.Revision < 1 || rules.Revision == long.MaxValue) throw new InvalidDataException("Quyền hội thoại không hợp lệ.");
            rules.Id = peerId; if (Store.SaveRestrictions(rules)) Changed?.Invoke(); return;
        }
        if (wire.Type == "lookup")
        {
            if (!Protocol.ValidId(wire.Id) || wire.Name.Length is < 2 or > 80 || wire.Name.Any(char.IsControl)) return;
            if (lastLookup.TryGetValue(peerId, out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(1)) return;
            lastLookup[peerId] = DateTimeOffset.UtcNow;
            await SendTo(peerId, new WireMessage { Type = "lookup-result", Id = wire.Id, Data = Codec.Encode(MatchingContacts(wire.Name).Where(c => c.Id != peerId).ToArray()) }); return;
        }
        if (wire.Type == "lookup-result")
        {
            if (!contactSearches.TryGetValue(wire.Id, out var search) || !search.Remaining.ContainsKey(peerId)) return;
            var cards = Codec.Decode<List<ContactCard>>(wire.Data); if (cards.Count > 20) throw new InvalidDataException("Quá nhiều kết quả tìm kiếm.");
            foreach (var card in cards) { ValidateContact(card); if (search.Results.Count < 100) search.Results[card.Id] = card; }
            search.Remaining.TryRemove(peerId, out _); if (search.Remaining.IsEmpty) search.Done.TrySetResult(); return;
        }
        if (wire.Type == "expiry-policy")
        {
            var policy = Codec.Decode<ConversationPolicy>(wire.Data);
            if (policy.Id is null || policy.Actor != peerId || policy.Revision < 1 || policy.Revision == long.MaxValue || policy.DisappearSeconds != 0 && policy.DisappearSeconds is < 10 or > 604800) throw new InvalidDataException("Cài đặt tự hủy không hợp lệ.");
            if (policy.Id.Length == 0) policy.Id = peerId;
            else if (!Store.Groups().Any(g => g.Id == policy.Id && g.Members.Contains(peerId) && g.Members.Contains(Identity.Id))) return;
            if (Store.SavePolicy(policy)) Changed?.Invoke(); return;
        }
        if (wire.Type == "expiry-start")
        {
            var message = Store.Message(wire.Id);
            if (message is null || message.DisappearAfterSeconds == 0 || !ConversationHasPeer(message, peerId)) return;
            var deadline = DateTimeOffset.FromUnixTimeMilliseconds(wire.Offset);
            if (deadline > DateTimeOffset.UtcNow.AddSeconds(message.DisappearAfterSeconds + 30)) return;
            if (Store.SetMessageExpiry(message.Id, deadline)) Changed?.Invoke(); return;
        }
        if (wire.Type == "presence")
        {
            if (wire.Data is not ("online" or "idle" or "dnd")) throw new InvalidDataException("Trạng thái không hợp lệ.");
            var peer = peers[peerId]; if (peer.Presence != wire.Data) { peer.Presence = wire.Data; Changed?.Invoke(); } return;
        }
        if (wire.Type == "reaction")
        {
            if (!Protocol.ValidId(wire.Id) || wire.Offset < 1 || wire.Data.Length > 0 && !ReactionEmoji.Contains(wire.Data)) throw new InvalidDataException("Biểu cảm không hợp lệ.");
            var message = Store.Message(wire.Id); if (message is null || !ConversationHasPeer(message, peerId)) return;
            bool changed = false;
            Store.UpdateMessage(wire.Id, current =>
            {
                if (wire.Offset <= current.ReactionVersions.GetValueOrDefault(peerId)) return;
                current.Reactions[peerId] = wire.Data; current.ReactionVersions[peerId] = wire.Offset; changed = true;
            });
            if (changed) Changed?.Invoke(); return;
        }
        if (wire.Type == "profile")
        {
            var name = ValidateDisplayName(wire.Name);
            ProfileAvatar.Validate(wire.Data);
            var peer = peers[peerId]; lock (peer) { peer.Name = name; peer.Avatar = wire.Data; Store.SavePeer(peer); }
            Changed?.Invoke(); return;
        }
        if (wire.Type == "ready") { Track(Replay(peerId)); return; }
        if (wire.Type == "group" && wire.Group is { } group)
        {
            if (group.OwnerId != peerId || !group.Members.Contains(Identity.Id)) throw new InvalidDataException("Nhóm không hợp lệ.");
            if (Store.ApplyGroup(group))
            {
                PrepareGroupHistory(group);
                foreach (var id in group.Members.Where(id => id != Identity.Id)) Track(Replay(id));
                Changed?.Invoke();
            }
            return;
        }
        if (wire.Type is "message" or "offer" or "contact" && wire.Message is { } incoming)
        {
            if (!Protocol.ValidId(incoming.Id) || incoming.SenderId != peerId || incoming.Text.Length > 16000 || incoming.ReplyToId.Length > 0 && !Protocol.ValidId(incoming.ReplyToId)) throw new InvalidDataException("Tin nhắn không hợp lệ.");
            if (Store.WasDeleted(incoming.Id)) { await SendTo(peerId, new WireMessage { Type = wire.Type is "message" or "contact" ? "ack" : "decline", Id = incoming.Id }); return; }
            bool history = false;
            if (incoming.GroupRevision < 0) throw new InvalidDataException("Phiên bản tin nhóm không hợp lệ.");
            if (incoming.GroupId.Length > 0)
            {
                var target = Store.Groups().Find(g => g.Id == incoming.GroupId);
                if (target is null || !target.Members.Contains(peerId) || !GroupMembership.CanRead(target, Identity.Id, incoming) || target.MemberAccess.TryGetValue(peerId, out var senderAccess) && incoming.GroupRevision < senderAccess.JoinedRevision) return;
                history = GroupMembership.IsHistory(target, Identity.Id, incoming);
            }
            var old = Store.Message(incoming.Id);
            if (old is not null)
            {
                if (old.SenderId != peerId || old.Outgoing) throw new InvalidDataException("ID tin nhắn bị trùng danh tính.");
                if (old.Kind is "text" or "contact") await SendTo(peerId, new WireMessage { Type = old.Status == "read" ? "read" : "ack", Id = incoming.Id });
                else if (old.Status == "received") await SendTo(peerId, new WireMessage { Type = "fileDone", Id = incoming.Id });
                return;
            }
            if (incoming.DisappearAfterSeconds != 0 && incoming.DisappearAfterSeconds is < 10 or > 604800) throw new InvalidDataException("Thời gian tự hủy không hợp lệ.");
            incoming.Outgoing = false; incoming.ExpiresAt = history && incoming.DisappearAfterSeconds > 0 ? incoming.ExpiresAt : null; incoming.ConversationId = incoming.GroupId.Length > 0 ? incoming.GroupId : peerId;
            if (incoming.ExpiresAt <= DateTimeOffset.UtcNow) return;
            if (incoming.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(incoming.DisappearAfterSeconds + 30)) throw new InvalidDataException("Hạn tự hủy không hợp lệ.");
            incoming.SenderName = peers[peerId].Name; incoming.Delivery.Clear(); incoming.Reactions.Clear(); incoming.ReactionVersions.Clear(); incoming.FilePath = ""; incoming.Error = ""; incoming.Transferred = 0;
            if (incoming.Time > DateTimeOffset.UtcNow.AddMinutes(5) || incoming.Time < DateTimeOffset.UtcNow.AddYears(-10)) incoming.Time = DateTimeOffset.UtcNow;
            if (wire.Type == "message") { incoming.Kind = "text"; incoming.Status = "delivered"; }
            else if (wire.Type == "contact") { ValidateContact(incoming.Contact ?? throw new InvalidDataException("Thiếu liên hệ.")); incoming.Kind = "contact"; incoming.Status = "delivered"; }
            else
            {
                if (incoming.FileSize < 0 || incoming.FileSize > Protocol.MaxFileSize || incoming.Hash.Length != 64 || !incoming.Hash.All(Uri.IsHexDigit) || incoming.Kind is not ("file" or "image" or "video")) throw new InvalidDataException("Thông tin tệp không hợp lệ.");
                incoming.FileName = Protocol.SafeFileName(incoming.FileName);
                incoming.FilePath = Path.Combine(Store.MediaPath, incoming.Id + "_" + incoming.FileName); incoming.Status = "offered";
            }
            if (history && incoming.Kind is "text" or "contact") incoming.Status = "read";
            // Unread expiring history still needs a real read to start its timer.
            var unread = !history || incoming.DisappearAfterSeconds > 0 && !incoming.ExpiresAt.HasValue;
            if (Store.SaveIncomingMessage(incoming, unread)) { Changed?.Invoke(); if (!history) MessageReceived?.Invoke(incoming); }
            if (incoming.Kind == "image") Track(ReceiveImagesAsync());
            if (wire.Type is "message" or "contact") await SendTo(peerId, new WireMessage { Type = "ack", Id = incoming.Id });
            return;
        }
        if (wire.Type is "ack" or "read" or "fileDone" or "decline")
        {
            var msg = Store.Message(wire.Id);
            if (msg is null || !msg.Outgoing || !msg.Delivery.ContainsKey(peerId)) return;
            var next = wire.Type switch { "read" => "read", "fileDone" => "delivered", "decline" => "declined", _ => "delivered" };
            Store.UpdateMessage(msg.Id, current =>
            {
                if (current.Delivery[peerId] != "read") current.Delivery[peerId] = next;
                current.Status = current.Delivery.Values.All(s => s == "read") ? "read" : current.Delivery.Values.All(s => s is "read" or "delivered") ? "delivered" : current.Delivery.Values.Any(s => s == "declined") ? "declined" : "queued";
            });
            Changed?.Invoke(); return;
        }
        if (wire.Type == "accept") { Track(SendFile(session, wire.Id, wire.Offset)); return; }
        if (wire.Type == "chunk")
        {
            var msg = Store.Message(wire.Id);
            if (msg is null || msg.Outgoing || msg.SenderId != peerId || !receiving.ContainsKey(msg.Id)) return;
            var bytes = Convert.FromBase64String(wire.Data);
            var part = msg.FilePath + ".part";
            await using var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, Protocol.ChunkSize, true);
            if (wire.Offset != file.Length || bytes.Length > Protocol.ChunkSize || file.Length + bytes.Length > msg.FileSize) throw new InvalidDataException("Phần tệp không hợp lệ.");
            file.Seek(0, SeekOrigin.End); await file.WriteAsync(bytes, lifetime.Token);
            TransferProgress?.Invoke(new TransferUpdate(msg.Id, file.Length, msg.FileSize)); return;
        }
        if (wire.Type == "end")
        {
            var msg = Store.Message(wire.Id);
            if (msg is null || msg.Outgoing || msg.SenderId != peerId || !receiving.ContainsKey(msg.Id)) return;
            try
            {
                var part = msg.FilePath + ".part";
                string hash; long size;
                await using (var file = File.OpenRead(part)) { size = file.Length; hash = Convert.ToHexString(await SHA256.HashDataAsync(file, lifetime.Token)); }
                if (size != msg.FileSize || hash != msg.Hash) { File.Delete(part); Store.UpdateMessage(msg.Id, current => { current.Status = "error"; current.Error = "Tệp không khớp mã kiểm tra. Hãy tải lại."; }); Changed?.Invoke(); return; }
                Store.UpdateMessage(msg.Id, current => { File.Move(part, msg.FilePath, true); current.Transferred = size; current.Status = "received"; });
                // Preserve Windows' downloaded-file safety treatment; attachments are never executed automatically.
                try { await File.WriteAllTextAsync(msg.FilePath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n", lifetime.Token); } catch (IOException) { }
                await SendTo(peerId, new WireMessage { Type = "fileDone", Id = msg.Id }); Changed?.Invoke();
            }
            finally { receiving.TryRemove(msg.Id, out _); Track(ReceiveImagesAsync()); }
        }
    }
    private async Task SendFile(Session session, string messageId, long offset)
    {
        var key = session.PeerId + ":" + messageId;
        if (!sending.TryAdd(key, 0)) return;
        try
        {
            var msg = Store.Message(messageId);
            if (msg is null || !msg.Outgoing || msg.Kind == "text" || !msg.Delivery.ContainsKey(session.PeerId) || !ConversationHasPeer(msg, session.PeerId) || offset < 0 || offset > msg.FileSize) return;
            await using var input = new FileStream(msg.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, Protocol.ChunkSize, true);
            input.Position = offset; byte[] buffer = new byte[Protocol.ChunkSize]; int count;
            while ((count = await input.ReadAsync(buffer, lifetime.Token)) > 0)
            {
                if (!peers.TryGetValue(session.PeerId, out var peer) || !peer.Trusted || peer.Blocked) return;
                var current = Store.Message(messageId);
                if (current is null || current.Delivery.GetValueOrDefault(session.PeerId) == "declined") return;
                await session.SendAsync(new WireMessage { Type = "chunk", Id = msg.Id, Offset = offset, Data = Convert.ToBase64String(buffer, 0, count) }, lifetime.Token);
                offset += count; TransferProgress?.Invoke(new TransferUpdate(msg.Id, offset, msg.FileSize));
            }
            await session.SendAsync(new WireMessage { Type = "end", Id = msg.Id }, lifetime.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { Notice?.Invoke("Truyền tệp tạm ngắt. Người nhận có thể bấm tiếp tục."); }
        finally { sending.TryRemove(key, out _); }
    }
    public async Task DeleteConversationHistoryAsync(string id)
    {
        var messages = Store.DeleteConversation(id);
        await DeleteStoredAttachments(messages);
    }
    public async Task DeleteMessagesAsync(string conversation, IReadOnlySet<string> ids)
    {
        var messages = Store.DeleteMessages(conversation, ids);
        await DeleteStoredAttachments(messages);
    }
    private async Task DeleteStoredAttachments(List<ChatMessage> messages)
    {
        // Publish the authoritative history change even if a media file is busy.
        Changed?.Invoke();
        foreach (var msg in messages)
        {
            receiving.TryRemove(msg.Id, out _);
            if (msg.Kind == "text" || string.IsNullOrEmpty(msg.FilePath)) continue;
            foreach (var file in new[] { msg.FilePath, msg.FilePath + ".part" })
            {
                var full = Path.GetFullPath(file);
                if (!full.StartsWith(Path.GetFullPath(Store.MediaPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (Store.Messages(limit: int.MaxValue).Any(m => string.Equals(m.FilePath, msg.FilePath, StringComparison.OrdinalIgnoreCase))) continue;
                for (var attempt = 0; attempt < 10; attempt++)
                { try { File.Delete(full); break; } catch (IOException) when (attempt < 9) { await Task.Delay(100); } }
            }
        }
        Changed?.Invoke();
    }
    private async Task QuietConnect(Peer peer)
    {
        try { await ConnectAsync(peer.Address, peer.Port, peer.Id); }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException or IOException) { }
    }
    private async Task MaintenanceLoop(bool announce)
    {
        try
        {
            var cycle = 0;
            while (!lifetime.IsCancellationRequested)
            {
                if (announce) await AnnounceAsync();
                foreach (var session in sessions.Values) await SendTo(session.PeerId, new WireMessage { Type = "ping" });
                if (++cycle % 3 == 0) foreach (var session in sessions.Values.Where(s => peers.TryGetValue(s.PeerId, out var p) && p.Trusted && !p.Blocked)) Track(Replay(session.PeerId, false));
                foreach (var peer in peers.Values.Where(p => p.Trusted && !p.Blocked && !p.Connected && p.Port > 0)) Track(QuietConnect(peer));
                Track(ReceiveImagesAsync());
                await Task.Delay(TimeSpan.FromSeconds(6), lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task ExpiryLoop()
    {
        try { while (!lifetime.IsCancellationRequested) { await ExpireMessagesAsync(); await Task.Delay(1000, lifetime.Token); } }
        catch (OperationCanceledException) { }
    }
    public async Task ExpireMessagesAsync()
    {
        var expired = Store.RemoveExpired(DateTimeOffset.UtcNow);
        if (expired.Count > 0) Changed?.Invoke();
        foreach (var message in expired)
        {
            receiving.TryRemove(message.Id, out _);
            if (message.FilePath.Length == 0) continue;
            foreach (var path in new[] { message.FilePath, message.FilePath + ".part" })
            {
                var full = Path.GetFullPath(path);
                if (!full.StartsWith(Path.GetFullPath(Store.MediaPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                for (int attempt = 0; attempt < 10; attempt++)
                { try { File.Delete(full); break; } catch (IOException) { await Task.Delay(150, lifetime.Token); } catch (UnauthorizedAccessException) { break; } }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync(); listener?.Stop(); discovery?.Dispose();
        foreach (var session in sessions.Values) session.Dispose();
        var pending = tasks.Values.Concat(new[] { acceptTask, discoverTask, announceTask, expiryTask }.OfType<Task>()).ToArray();
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or SocketException or ObjectDisposedException) { }
        lifetime.Dispose();
    }
}
