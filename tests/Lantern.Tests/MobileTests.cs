using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Lantern.Core;

internal static class MobileTests
{
    public static async Task Run(string root, Action<bool, string> check, RelayConfiguration? relay = null)
    {
        var longHistory = Enumerable.Range(0, 50).Select(i => new ChatMessage { Id = i.ToString("x32"), Text = new string('ễ', 16000), FilePath = "private-pc-path" });
        var bounded = new MobilePacket { Messages = MobileProtocol.BoundedHistory(longHistory) };
        check(JsonSerializer.SerializeToUtf8Bytes(bounded, MobileProtocol.Json).Length <= MobileProtocol.MaxFrame && bounded.Messages.Last().Id == 49.ToString("x32") && bounded.Messages.All(m => m.FilePath.Length == 0), "Long Unicode history stays within mobile frame size while keeping the newest messages");
        var path = Path.Combine(root, "mobile-" + Guid.NewGuid().ToString("N"));
        using var vault = WorkspaceVault.Create(path, "Mobile-test-password!");
        var forged = Path.Combine(path, "forged"); Directory.CreateDirectory(forged); File.WriteAllText(Path.Combine(forged, "mobile-devices.enc"), "{\"devices\":[]}");
        bool plaintextRejected = false; try { _ = new MobileRegistry(forged, vault); } catch (InvalidDataException) { plaintextRejected = true; }
        check(plaintextRejected, "Plaintext device authorization cannot bypass the encrypted registry");
        using var identity = new Identity(path, "PC thử nghiệm", vault);
        using var store = new LocalStore(path, vault);
        await using var node = new PeerNode(identity, store);
        var otherPath = Path.Combine(path, "other"); using var otherId = new Identity(otherPath, "Đối tác"); using var otherStore = new LocalStore(otherPath);
        await using var other = new PeerNode(otherId, otherStore);
        await node.StartAsync(0, false, true); await other.StartAsync(0, false, true);
        await node.ConnectAsync("127.0.0.1", other.Port, otherId.Id); await node.TrustAsync(otherId.Id); await other.TrustAsync(identity.Id);
        await using var server = new MobileServer(node, vault); if (relay is not null) server.ConfigureRelay(relay);
        server.ApprovePairing = _ => Task.FromResult(true); await server.StartAsync(0, true);
        using var phone = new Identity(Path.Combine(path, "phone"), "Điện thoại QA");
        using var invitation = JsonDocument.Parse(await server.CreateInvitationAsync());
        var qr = invitation.RootElement;
        using var tcp = new TcpClient(); RelayApi? api = null; HttpRelayStream? tunnel = null;
        Stream stream;
        MobileRoute? route = null;
        if (relay is null) { await tcp.ConnectAsync("127.0.0.1", server.Port); stream = tcp.GetStream(); }
        else
        {
            var r = qr.GetProperty("relay"); route = new() { Id = r.GetProperty("id").GetString()!, ClientKey = r.GetProperty("key").GetString()! };
            api = new RelayApi(relay); var session = Guid.NewGuid().ToString("N"); await api.Call("v1/routes/" + route.Id + "/open", route.ClientKey, new { session }, CancellationToken.None);
            tunnel = new HttpRelayStream(api, route, session, false, CancellationToken.None); stream = tunnel;
        }
        using var ssl = new SslStream(stream, true, (_, cert, _, _) => cert is not null && Identity.PeerId(new X509Certificate2(cert)) == identity.Id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "lantern.local", ClientCertificates = new X509CertificateCollection { phone.Certificate }, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, timeout.Token);
            async Task<MobilePacket> Call(MobilePacket packet) { await MobileProtocol.WriteAsync(ssl, packet, timeout.Token); return await MobileProtocol.ReadAsync(ssl, timeout.Token); }
            var welcome = await Call(new() { Type = "hello", Nonce = qr.GetProperty("nonce").GetString()!, Name = "Android QA" });
            check(welcome.Type == "welcome" && welcome.Id == identity.Id, relay is null ? "Mobile QR pairing uses mutually authenticated TLS" : "Public managed relay carries end-to-end mutual TLS successfully");
            check(server.Registry.Contains(phone.Id), "Only approved phone certificate is persisted");
            var registryText = File.ReadAllText(Path.Combine(path, "mobile-devices.enc"));
            check(!registryText.Contains(phone.Id) && (relay is null || !registryText.Contains(relay.ServiceToken)), "Device credentials are encrypted in the workspace");
            check((await Call(new() { Type = "list" })).Conversations.Any(c => c.Id == otherId.Id), "Phone uses the PC contact list");
            vault.SetConversationPassword(otherId.Id, "conversation-secret");
            check((await Call(new() { Type = "list" })).Conversations.Single(c => c.Id == otherId.Id).Protected, "Phone knows protected conversations must stay locked in offline cache");
            check((await Call(new() { Type = "history", Conversation = otherId.Id })).Type == "error", "Locked conversation denies mobile history");
            check((await Call(new() { Type = "send", Id = Guid.NewGuid().ToString("N"), Conversation = otherId.Id, Text = "blocked" })).Type == "error", "Locked conversation denies mobile sending");
            check((await Call(new() { Type = "unlock", Conversation = otherId.Id, Password = "wrong" })).Type == "error", "Wrong conversation password is rejected");
            check((await Call(new() { Type = "unlock", Conversation = otherId.Id, Password = "conversation-secret" })).Type == "ok", "Phone unlock verifies the PC password");
            var requestId = Guid.NewGuid().ToString("N");
            var sent = await Call(new() { Type = "send", Id = requestId, Conversation = otherId.Id, Text = "Xin chào từ điện thoại 🙂" });
            var duplicate = await Call(new() { Type = "send", Id = requestId, Conversation = otherId.Id, Text = "Xin chào từ điện thoại 🙂" });
            check(sent.MessageId.Length == 32 && sent.MessageId == duplicate.MessageId && store.Messages(otherId.Id).Count(m => m.Id == sent.MessageId) == 1, "Retrying a mobile send does not duplicate the message");
            await Task.Delay(300);
            check(otherStore.Message(sent.MessageId)?.SenderId == identity.Id, "LAN partner receives phone message from the PC identity");
            var history = await Call(new() { Type = "history", Conversation = otherId.Id });
            check(history.Messages.Count == 1 && history.Messages.All(m => m.FilePath == ""), "History is authorized and never exposes PC file paths");
            var unchanged = await Call(new() { Type = "history", Conversation = otherId.Id, Hash = history.Hash });
            check(history.Hash.Length == 64 && unchanged.Type == "unchanged" && unchanged.Messages.Count == 0, "Unchanged history avoids downloading the same messages again");
            await Call(new() { Type = "list" });
            check((await Call(new() { Type = "history", Conversation = otherId.Id })).Type == "ok", "Switching screens retains conversation unlock within session");
            var reply = await Call(new() { Type = "send", Id = Guid.NewGuid().ToString("N"), Conversation = otherId.Id, Text = "Trích dẫn", ReplyTo = sent.MessageId });
            check(store.Message(reply.MessageId)?.ReplyToId == sent.MessageId, "Mobile quote reaches PC history");
            await Call(new() { Type = "react", Conversation = otherId.Id, MessageId = sent.MessageId, Emoji = "❤️" });
            check(store.Message(sent.MessageId)?.Reactions.GetValueOrDefault(identity.Id) == "❤️", "Phone reaction uses PC identity");
            check((await Call(new() { Type = "history", Conversation = otherId.Id, Hash = history.Hash })).Type == "ok", "Changed history invalidates the phone snapshot hash");
            var shown = await other.SendTextAsync(identity.Id, "Đã hiển thị trên điện thoại");
            var unseen = await other.SendTextAsync(identity.Id, "Chưa hiển thị trên điện thoại");
            for (int i = 0; i < 100 && store.Message(unseen.Id) is null; i++) await Task.Delay(30);
            await Call(new() { Type = "read", Conversation = otherId.Id, VisibleIds = [shown.Id] });
            check(store.Message(shown.Id)?.Status == "read" && store.Message(unseen.Id)?.Status != "read", "Mobile read acknowledgements exclude messages not displayed on the phone");
            var bytes = RandomNumberGenerator.GetBytes(40000); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await Call(new() { Type = "upload-start", Id = Guid.NewGuid().ToString("N"), Conversation = otherId.Id, FileName = "qa.bin", Size = bytes.Length, Hash = hash });
            check((await Call(new() { Type = "upload-chunk", Conversation = otherId.Id, Offset = 1, Data = Convert.ToBase64String(bytes[..32768]) })).Type == "error", "Out-of-order upload is rejected");
            await Call(new() { Type = "upload-chunk", Conversation = otherId.Id, Data = Convert.ToBase64String(bytes[..32768]) });
            await Call(new() { Type = "upload-chunk", Conversation = otherId.Id, Offset = 32768, Data = Convert.ToBase64String(bytes[32768..]) });
            var upload = await Call(new() { Type = "upload-finish", Conversation = otherId.Id, FileName = "qa.bin" });
            check(store.Message(upload.MessageId)?.Hash == hash, "Mobile file upload checks length and SHA-256 before offering to LAN peer");
            var chunk = await Call(new() { Type = "download", Conversation = otherId.Id, MessageId = upload.MessageId });
            check(Convert.FromBase64String(chunk.Data).SequenceEqual(bytes[..32768]), "Mobile attachment download returns verified message-owned bytes");
            server.LockConversation(otherId.Id);
            check((await Call(new() { Type = "download", Conversation = otherId.Id, MessageId = upload.MessageId })).Type == "error", "PC manual lock also denies mobile attachment access");
            await server.RevokeAsync(phone.Id);
            bool denied = false; try { await Call(new() { Type = "list" }); } catch (Exception) { denied = true; }
            check(denied && !server.Registry.Contains(phone.Id), "Revocation closes an active phone connection");
        }
        finally { if (tunnel is not null) await tunnel.DisposeAsync(); api?.Dispose(); }
        check(new MobileRegistry(path, vault).Snapshot().Devices.Count == 0, "Device revocation survives a restart");
        using var attacker = new Identity(Path.Combine(path, "unapproved"), "Unapproved");
        using var unauthorizedTcp = new TcpClient(); await unauthorizedTcp.ConnectAsync("127.0.0.1", server.Port);
        using var unauthorized = new SslStream(unauthorizedTcp.GetStream(), false, (_, _, _, _) => true);
        using var denialTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await unauthorized.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "lantern.local", ClientCertificates = new X509CertificateCollection { attacker.Certificate }, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, denialTimeout.Token);
        bool unauthorizedDenied = false;
        try { await MobileProtocol.WriteAsync(unauthorized, new() { Type = "hello", Name = "Unapproved", Nonce = qr.GetProperty("nonce").GetString()! }, denialTimeout.Token); await MobileProtocol.ReadAsync(unauthorized, denialTimeout.Token); } catch (Exception) { unauthorizedDenied = true; }
        check(unauthorizedDenied && !server.Registry.Contains(attacker.Id), "An unapproved certificate cannot replay a consumed QR nonce");
        int approvals = 0; server.ApprovePairing = _ => { approvals++; return Task.FromResult(true); };
        async Task<(TcpClient Tcp, SslStream Tls)> PairAgain(string name)
        {
            using var code = JsonDocument.Parse(await server.CreateInvitationAsync()); var socket = new TcpClient(); await socket.ConnectAsync("127.0.0.1", server.Port);
            var tls = new SslStream(socket.GetStream(), false, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "lantern.local", ClientCertificates = new X509CertificateCollection { phone.Certificate }, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck });
            await MobileProtocol.WriteAsync(tls, new() { Type = "hello", Nonce = code.RootElement.GetProperty("nonce").GetString()!, Name = name }, CancellationToken.None);
            var result = await MobileProtocol.ReadAsync(tls, CancellationToken.None); if (result.Type != "welcome") throw new Exception("Pairing failed"); return (socket, tls);
        }
        var firstPair = await PairAgain("Phone first pairing");
        try
        {
            var secondPair = await PairAgain("Phone paired again"); using var secondTcp = secondPair.Tcp; using var secondTls = secondPair.Tls;
            check(approvals == 2 && server.Registry.Snapshot().Devices.Count == 1 && server.Registry.Snapshot().Devices.Single().Name == "Phone paired again", "Re-pairing the same phone requires PC approval and replaces its route without duplicate devices");
            bool oldClosed = false;
            try { using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await MobileProtocol.WriteAsync(firstPair.Tls, new() { Type = "list" }, limit.Token); await MobileProtocol.ReadAsync(firstPair.Tls, limit.Token); } catch (Exception) { oldClosed = true; }
            check(oldClosed, "Re-pairing closes the phone's previous connection");
        }
        finally { firstPair.Tls.Dispose(); firstPair.Tcp.Dispose(); await server.RevokeAsync(phone.Id); }
    }
}
