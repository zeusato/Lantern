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
while (true)
{
    using var tcp = await listener.AcceptTcpClientAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var ssl = new SslStream(tcp.GetStream(), false, (_, clientCertificate, _, _) => clientCertificate is not null);
    try
    {
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, ClientCertificateRequired = true, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, timeout.Token);
        using var remote = X509CertificateLoader.LoadCertificate(ssl.RemoteCertificate!.GetRawCertData());
        using var remoteKey = remote.GetECDsaPublicKey() ?? throw new AuthenticationException();
        var phone = Convert.ToHexString(SHA256.HashData(remoteKey.ExportSubjectPublicKeyInfo()));
        while (true)
        {
            var header = new byte[4]; await ssl.ReadExactlyAsync(header, timeout.Token); var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is < 2 or > 100000) throw new IOException();
            var data = new byte[length]; await ssl.ReadExactlyAsync(data, timeout.Token); using var request = JsonDocument.Parse(data);
            var type = request.RootElement.GetProperty("type").GetString();
            var response = JsonSerializer.SerializeToUtf8Bytes(new { type = type == "hello" ? "welcome" : "ok", id = pc, text = phone, name = "QA PC" });
            var frame = new byte[4 + response.Length]; BinaryPrimitives.WriteInt32BigEndian(frame, response.Length); response.CopyTo(frame, 4); await ssl.WriteAsync(frame, timeout.Token);
        }
    }
    catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException) { Console.Error.WriteLine(ex); }
}
