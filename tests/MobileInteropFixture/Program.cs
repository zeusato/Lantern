// Synthetic loopback-only fixture for Android/.NET TLS interoperability. Never packaged with Lantern.
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
using var certificate = new CertificateRequest("CN=Lantern QA", key, HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
var pc = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
var listener = new TcpListener(IPAddress.Loopback, 45854); listener.Start();
Console.WriteLine(JsonSerializer.Serialize(new { pc, port = 45854 }));
var imageBytes = RandomNumberGenerator.GetBytes(8 * 1024 * 1024);
var imageHash = Convert.ToHexString(SHA256.HashData(imageBytes));
var conversation = new string('a', 64);
var seedMessages = new List<Dictionary<string, object?>>
{
    new() { ["id"] = new string('1',32), ["kind"] = "text", ["text"] = "QA contact history", ["outgoing"] = false, ["senderName"] = "QA contact", ["time"] = DateTimeOffset.UtcNow, ["status"] = "delivered" },
    new() { ["id"] = new string('2',32), ["kind"] = "image", ["fileName"] = "slow-preview.png", ["fileSize"] = imageBytes.Length, ["hash"] = imageHash, ["outgoing"] = false, ["senderName"] = "QA contact", ["time"] = DateTimeOffset.UtcNow, ["status"] = "received" }
};
for (int i = 0; i < 28; i++) seedMessages.Insert(i, new() { ["id"] = (i + 100).ToString("x32"), ["kind"] = "text", ["text"] = "Earlier chat " + i, ["outgoing"] = i % 2 == 0, ["senderName"] = "QA contact", ["time"] = DateTimeOffset.UtcNow, ["status"] = "delivered" });
while (true)
{
    using var tcp = await listener.AcceptTcpClientAsync();
    tcp.NoDelay = true;
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    using var ssl = new SslStream(tcp.GetStream(), false, (_, clientCertificate, _, _) => clientCertificate is not null);
    try
    {
        var first = new byte[5];
        var count = await tcp.Client.ReceiveAsync(first, SocketFlags.Peek, timeout.Token);
        Console.Error.WriteLine($"TLS first bytes: {Convert.ToHexString(first.AsSpan(0, count))}");
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, ClientCertificateRequired = true, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, timeout.Token);
        using var remote = X509CertificateLoader.LoadCertificate(ssl.RemoteCertificate!.GetRawCertData());
        using var remoteKey = remote.GetECDsaPublicKey() ?? throw new AuthenticationException();
        var phone = Convert.ToHexString(SHA256.HashData(remoteKey.ExportSubjectPublicKeyInfo()));
        bool unlocked = false, pushEnabled = false, accountAuthenticated = false; long serial = 0; int inspections = 0;
        var messages = seedMessages.Select(m => new Dictionary<string, object?>(m)).ToList();
        string alias = ""; int disappearSeconds = 0; long historyVersion = 0;
        object Contact() => new { id = conversation, name = alias.Length > 0 ? alias : "QA contact", alias, originalName = "QA contact", presence = "online", unread = 0, locked = !unlocked, @protected = true, isGroup = false, disappearSeconds, historyVersion };
        while (true)
        {
            var header = new byte[4]; await ssl.ReadExactlyAsync(header, timeout.Token); var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is < 2 or > 100000) throw new IOException();
            var data = new byte[length]; await ssl.ReadExactlyAsync(data, timeout.Token); using var request = JsonDocument.Parse(data);
            var type = request.RootElement.GetProperty("type").GetString();
            if (type == "send") Console.Error.WriteLine("QA send: " + request.RootElement.GetProperty("text").GetString());
            // Match MobilePacket's serialized defaults, including empty contact arrays on non-list replies.
            var reply = new Dictionary<string, object?> { ["type"] = type == "hello" ? "welcome" : "ok", ["id"] = pc, ["text"] = phone, ["name"] = "QA PC", ["conversations"] = Array.Empty<object>() };
            if (type == "hello") { reply["capabilities"] = new[] { "sync-v1", "push-v1", "pc-v1", "conversation-settings-v1", "app-password-v1" }; pushEnabled = request.RootElement.TryGetProperty("capabilities", out var caps) && caps.EnumerateArray().Any(c => c.GetString() == "push-v1"); }
            else if (request.RootElement.TryGetProperty("id", out var requestId)) reply["id"] = requestId.GetString();
            reply["serial"] = ++serial; reply["conversation"] = request.RootElement.TryGetProperty("conversation", out var selected) ? selected.GetString() : "";
            if (type == "app-login") { accountAuthenticated = request.RootElement.GetProperty("password").GetString() == "qa-account-password"; reply["type"] = accountAuthenticated ? "app-authenticated" : "error"; if (!accountAuthenticated) reply["text"] = "Mật khẩu chưa đúng"; }
            else if (type == "app-logout") { accountAuthenticated = false; unlocked = false; reply["type"] = "app-locked"; }
            else if (type == "app-status") reply["type"] = accountAuthenticated ? "app-authenticated" : "app-locked";
            else if (type != "hello" && !accountAuthenticated) { reply["type"] = type is "sync" or "list" ? "app-locked" : "error"; reply["text"] = "Đăng nhập tài khoản trước"; }
            else
            {
            if (type == "pc")
            {
                reply["type"] = "pc";
                var action = request.RootElement.GetProperty("pc").GetProperty("action").GetString();
                if (action == "status") reply["pc"] = new { ai = true, power = false, roots = Array.Empty<object>() };
                else if (action == "ai.inspect")
                {
                    await Task.Delay(1200); inspections++;
                    if (inspections == 3) { reply["type"] = "error"; reply["text"] = "QA inspection error"; }
                    else reply["pc"] = new { targets = new[] {
                        new { id = "claude", app = "Claude", conversation = "QA Claude", state = inspections == 2 ? "unknown" : "busy", version = "", detail = inspections == 2 ? "QA provider timeout" : "AI đang xử lý." },
                        new { id = "codex", app = "ChatGPT / Codex", conversation = "QA Codex", state = "idle", version = "fixture", detail = "Sẵn sàng nhận yêu cầu." }
                    } };
                }
                else { reply["type"] = "error"; reply["text"] = "Fixture only supports read-only AI status."; }
            }
            bool changed = type is "conversation-rename" or "conversation-timer" or "delete-messages" or "clear-history";
            if (changed && !unlocked) { reply["type"] = "error"; reply["text"] = "Hội thoại đang khóa"; changed = false; }
            if (changed)
            {
                await Task.Delay(200);
                if (type == "conversation-rename") alias = request.RootElement.GetProperty("name").GetString()!.Trim();
                if (type == "conversation-timer") disappearSeconds = request.RootElement.GetProperty("disappearSeconds").GetInt32();
                if (type == "delete-messages") { var ids = request.RootElement.GetProperty("messageIds").EnumerateArray().Select(x => x.GetString()).ToHashSet(); messages.RemoveAll(m => ids.Contains((string?)m["id"])); historyVersion++; }
                if (type == "clear-history") { messages.Clear(); historyVersion++; }
            }
            if (type is "list" or "sync" || changed) { reply["conversations"] = new[] { Contact() }; reply["locked"] = !unlocked; }
            if (type == "unlock") { await Task.Delay(1200); unlocked = request.RootElement.GetProperty("password").GetString() == "qa-password"; if (!unlocked) { reply["type"] = "error"; reply["text"] = "Mật khẩu chưa đúng"; } }
            if (type == "lock") unlocked = false;
            if (type == "history" || changed || (type is "sync" or "unlock") && unlocked) { var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(messages))); reply["hash"] = hash; reply["messages"] = messages; if (request.RootElement.TryGetProperty("hash", out var previous) && previous.GetString() == hash) { reply["type"] = "unchanged"; reply["messages"] = Array.Empty<object>(); } }
            if (type == "send") { await Task.Delay(500); var id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(phone + request.RootElement.GetProperty("id").GetString())))[..32].ToLowerInvariant(); if (!messages.Any(m => (string?)m["id"] == id)) messages.Add(new() { ["id"] = id, ["kind"] = "text", ["text"] = request.RootElement.GetProperty("text").GetString(), ["outgoing"] = true, ["senderName"] = "QA PC", ["time"] = DateTimeOffset.UtcNow, ["status"] = "delivered" }); reply["messageId"] = id; reply["messages"] = messages.Where(m => (string?)m["id"] == id).ToArray(); }
            if (type == "download") { await Task.Delay(80); int offset = request.RootElement.GetProperty("offset").GetInt32(); reply["data"] = Convert.ToBase64String(imageBytes, offset, Math.Min(32768, imageBytes.Length - offset)); }
            }
            var response = JsonSerializer.SerializeToUtf8Bytes(reply);
            var frame = new byte[4 + response.Length]; BinaryPrimitives.WriteInt32BigEndian(frame, response.Length); response.CopyTo(frame, 4); await ssl.WriteAsync(frame, timeout.Token);
            if (type == "send" && pushEnabled && accountAuthenticated)
            {
                if (!messages.Any(m => (string?)m.GetValueOrDefault("text") == "Incoming immediately after send")) messages.Add(new() { ["id"] = new string('9',32), ["kind"] = "text", ["text"] = "Incoming immediately after send", ["outgoing"] = false, ["time"] = DateTimeOffset.UtcNow, ["status"] = "delivered" });
                var push = JsonSerializer.SerializeToUtf8Bytes(new { type = "ok", id = "", serial = ++serial, conversation, locked = !unlocked, hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(messages))), messages, conversations = new[] { Contact() } });
                var pushed = new byte[4 + push.Length]; BinaryPrimitives.WriteInt32BigEndian(pushed, push.Length); push.CopyTo(pushed, 4); await ssl.WriteAsync(pushed, timeout.Token);
            }
        }
    }
    catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException) { Console.Error.WriteLine(ex); }
}
