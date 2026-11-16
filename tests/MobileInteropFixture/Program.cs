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
var messages = new List<Dictionary<string, object?>>
{
    new() { ["id"] = new string('1',32), ["kind"] = "text", ["text"] = "QA contact history", ["outgoing"] = false, ["senderName"] = "QA contact", ["time"] = DateTimeOffset.UtcNow, ["status"] = "delivered" },
    new() { ["id"] = new string('2',32), ["kind"] = "image", ["fileName"] = "slow-preview.png", ["fileSize"] = imageBytes.Length, ["hash"] = imageHash, ["outgoing"] = false, ["senderName"] = "QA contact", ["time"] = DateTimeOffset.UtcNow, ["status"] = "received" }
};
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
        bool unlocked = false;
        while (true)
        {
            var header = new byte[4]; await ssl.ReadExactlyAsync(header, timeout.Token); var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is < 2 or > 100000) throw new IOException();
            var data = new byte[length]; await ssl.ReadExactlyAsync(data, timeout.Token); using var request = JsonDocument.Parse(data);
            var type = request.RootElement.GetProperty("type").GetString();
            var reply = new Dictionary<string, object?> { ["type"] = type == "hello" ? "welcome" : "ok", ["id"] = pc, ["text"] = phone, ["name"] = "QA PC" };
            if (type == "list") reply["conversations"] = new[] { new Dictionary<string, object?> { ["id"] = conversation, ["name"] = "QA contact", ["presence"] = "online", ["unread"] = 0, ["locked"] = !unlocked, ["protected"] = true } };
            if (type == "unlock") { await Task.Delay(1200); unlocked = request.RootElement.GetProperty("password").GetString() == "qa-password"; if (!unlocked) { reply["type"] = "error"; reply["text"] = "Mật khẩu chưa đúng"; } }
            if (type == "history") { var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(messages))); reply["hash"] = hash; reply["messages"] = messages; if (request.RootElement.TryGetProperty("hash", out var previous) && previous.GetString() == hash) { reply["type"] = "unchanged"; reply["messages"] = Array.Empty<object>(); } }
            if (type == "send") { await Task.Delay(500); var id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(phone + request.RootElement.GetProperty("id").GetString())))[..32].ToLowerInvariant(); if (!messages.Any(m => (string?)m["id"] == id)) messages.Add(new() { ["id"] = id, ["kind"] = "text", ["text"] = request.RootElement.GetProperty("text").GetString(), ["outgoing"] = true, ["senderName"] = "QA PC", ["time"] = DateTimeOffset.UtcNow, ["status"] = "delivered" }); reply["messageId"] = id; }
            if (type == "download") { await Task.Delay(80); int offset = request.RootElement.GetProperty("offset").GetInt32(); reply["data"] = Convert.ToBase64String(imageBytes, offset, Math.Min(32768, imageBytes.Length - offset)); }
            var response = JsonSerializer.SerializeToUtf8Bytes(reply);
            var frame = new byte[4 + response.Length]; BinaryPrimitives.WriteInt32BigEndian(frame, response.Length); response.CopyTo(frame, 4); await ssl.WriteAsync(frame, timeout.Token);
        }
    }
    catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException) { Console.Error.WriteLine(ex); }
}
