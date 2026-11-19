using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Lantern.Core;

internal static class MobileAppLoginTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        using var vault = WorkspaceVault.Create(root, "Account-password-123");
        using var identity = new Identity(root, "PC login fixture", vault);
        using var store = new LocalStore(root, vault);
        var peerId = new string('a', 64); store.SavePeer(new Peer { Id = peerId, Name = "Private contact", Trusted = true, Address = "127.0.0.1" });
        store.SaveMessage(new ChatMessage { ConversationId = peerId, SenderId = peerId, Text = "Private cached history" });
        await using var node = new PeerNode(identity, store);
        await using var server = new MobileServer(node, vault); await server.StartAsync(0, true);
        using var phone = new Identity(Path.Combine(root, "phone"), "Android");
        server.Registry.Add(new MobileDevice { Id = phone.Id, Name = "Android" });
        async Task<(TcpClient Tcp, SslStream Tls)> Connect(bool capability)
        {
            var tcp = new TcpClient(); await tcp.ConnectAsync("127.0.0.1", server.Port);
            var tls = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => cert is not null && Identity.PeerId(new X509Certificate2(cert)) == identity.Id);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "lantern.local", ClientCertificates = new X509CertificateCollection { phone.Certificate }, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck });
            await MobileProtocol.WriteAsync(tls, new() { Type = "hello", Name = "Android", Capabilities = capability ? ["app-password-v1"] : [] }, CancellationToken.None);
            var welcome = await MobileProtocol.ReadAsync(tls, CancellationToken.None);
            check(welcome.Type == "welcome" && welcome.Capabilities.Contains("app-password-v1"), "PC advertises account-password authentication to paired Android");
            return (tcp, tls);
        }
        async Task<MobilePacket> Call(SslStream tls, string type, string password = "")
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await MobileProtocol.WriteAsync(tls, new() { Type = type, Password = password, Conversation = peerId, Text = "forbidden", Pc = type == "pc" ? new() { Action = "ai.inspect" } : null }, limit.Token);
            return await MobileProtocol.ReadAsync(tls, limit.Token);
        }
        var first = await Connect(true);
        using (first.Tcp) using (first.Tls)
        {
            var locked = await Call(first.Tls, "sync");
            check(locked.Type == "app-locked" && locked.Conversations.Count == 0 && locked.Messages.Count == 0 && !locked.ConversationSnapshot, "Before login no conversation names, cached history or destructive empty snapshot is returned");
            foreach (var type in new[] { "history", "send", "unlock", "download", "delete-messages", "conversation-rename", "pc" })
            {
                var denied = await Call(first.Tls, type);
                check(denied.Type == "error" && denied.Text.Contains("Đăng nhập"), "Account gate denies pre-login " + type);
            }
            check((await Call(first.Tls, "app-login", "Wrong-password")).Type == "error", "An incorrect account password cannot unlock Android");
            check((await Call(first.Tls, "app-login", "Account-password-123")).Type == "error", "Repeated account-password attempts are throttled");
            await Task.Delay(2100);
            check((await Call(first.Tls, "app-login", "Account-password-123")).Type == "app-authenticated", "The PC account password unlocks the authenticated phone session");
            check((await Call(first.Tls, "list")).Conversations.Single().Name == "Private contact", "Contacts appear only after account authentication");
            await Call(first.Tls, "app-logout");
            check((await Call(first.Tls, "history")).Type == "error" && (await Call(first.Tls, "list")).Type == "app-locked", "Logging out removes access to contacts and history immediately");
            vault.ChangePassword("Account-password-123", "Changed-password-456");
            check((await Call(first.Tls, "app-login", "Account-password-123")).Type == "error", "Android rejects the previous PC password after a password change");
            await Task.Delay(2100);
            check((await Call(first.Tls, "app-login", "Changed-password-456")).Type == "app-authenticated", "Android verifies the current PC password without a cached verifier");
        }
        var second = await Connect(false);
        using (second.Tcp) using (second.Tls)
            check((await Call(second.Tls, "list")).Type == "app-locked", "Reconnect requires login again and cannot downgrade by omitting the capability");
        var reopened = new MobileRegistry(root, vault);
        check(reopened.Snapshot().Devices.Single().RequireAppPassword && !File.ReadAllText(Path.Combine(root, "mobile-devices.enc")).Contains("Changed-password"), "The device password requirement persists without storing the password");
    }
}
