using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Lantern.Core;

public sealed class Identity : IDisposable
{
    public X509Certificate2 Certificate { get; }
    public string Id { get; }
    public string Name { get; set; }
    public string Fingerprint => string.Join(" ", Enumerable.Range(0, 8).Select(i => Id.Substring(i * 8, 8)));
    public Identity(string directory, string name, WorkspaceVault? vault = null)
    {
        Directory.CreateDirectory(directory);
        Name = name;
        var path = Path.Combine(directory, "identity.bin");
        if (File.Exists(path))
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            var envelope = Encoding.UTF8.GetString(plain);
            if (envelope.StartsWith("enc:v1:", StringComparison.Ordinal))
            {
                if (vault is null) { CryptographicOperations.ZeroMemory(plain); throw new UnauthorizedAccessException("Cần mật khẩu workspace để mở khóa danh tính."); }
                var decoded = Convert.FromBase64String(vault.Unseal(envelope)); CryptographicOperations.ZeroMemory(plain); plain = decoded;
            }
            // Schannel needs an OS-backed key handle. DefaultKeySet creates a temporary
            // user key container which .NET removes when this certificate is disposed.
            try { Certificate = X509CertificateLoader.LoadPkcs12(plain, null, X509KeyStorageFlags.Exportable); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (vault is not null && !envelope.StartsWith("enc:v1:", StringComparison.Ordinal)) SaveCertificate(path, Certificate, vault);
        }
        else
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Lantern Peer", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
            var portable = generated.Export(X509ContentType.Pfx);
            try { Certificate = X509CertificateLoader.LoadPkcs12(portable, null, X509KeyStorageFlags.Exportable); }
            finally { CryptographicOperations.ZeroMemory(portable); }
            SaveCertificate(path, Certificate, vault);
        }
        Id = PeerId(Certificate);
    }
    public static string PeerId(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
    public static string VerificationCode(string first, string second)
    {
        var pair = new[] { first, second }.Order(StringComparer.Ordinal);
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Lantern-pair-v1:" + string.Join(":", pair))))[..24];
        return string.Join(" ", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 4, 4)));
    }
    private static void SaveCertificate(string path, X509Certificate2 certificate, WorkspaceVault? vault = null)
    {
        var plain = certificate.Export(X509ContentType.Pfx);
        try
        {
            if (vault is not null) { var wrapped = Encoding.UTF8.GetBytes(vault.Seal(Convert.ToBase64String(plain))); CryptographicOperations.ZeroMemory(plain); plain = wrapped; }
            File.WriteAllBytes(path, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Export(string file, string password)
    {
        if (password.Length < 12) throw new ArgumentException("Mật khẩu sao lưu cần ít nhất 12 ký tự.");
        File.WriteAllBytes(file, Certificate.Export(X509ContentType.Pfx, password));
    }
    public static void Import(string file, string password, string directory, WorkspaceVault? vault = null)
    {
        using var cert = X509CertificateLoader.LoadPkcs12FromFile(file, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
        if (!cert.HasPrivateKey || cert.GetECDsaPrivateKey() is null) throw new InvalidDataException("Bản sao lưu không có khóa Lantern hợp lệ.");
        Directory.CreateDirectory(directory);
        SaveCertificate(Path.Combine(directory, "identity.bin"), cert, vault);
    }
    public void Dispose() => Certificate.Dispose();
}
