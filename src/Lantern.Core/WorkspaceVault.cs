using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Lantern.Core;

public sealed class VaultHeader
{
    public int Version { get; set; } = 1;
    public string WorkspaceId { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerSid { get; set; } = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Không xác định được tài khoản Windows.");
    public string OwnerProof { get; set; } = "";
    public int Iterations { get; set; } = 600000;
    public string Salt { get; set; } = "";
    public string WrappedKey { get; set; } = "";
}

public sealed class PasswordRecord
{
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public int Iterations { get; set; } = 600000;
    public static PasswordRecord Create(string password)
    {
        if (password.Length < 8) throw new ArgumentException("Mật khẩu cần ít nhất 8 ký tự.");
        var record = new PasswordRecord { Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) };
        record.Hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(record.Salt), record.Iterations, HashAlgorithmName.SHA256, 32)); return record;
    }
    public bool Verify(string password)
    {
        if (Iterations is < 600000 or > 2000000) return false;
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(Salt), Iterations, HashAlgorithmName.SHA256, 32);
        try { return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(Hash)); }
        finally { CryptographicOperations.ZeroMemory(actual); }
    }
}

public sealed class WorkspaceVault : IDisposable
{
    private readonly byte[] key;
    private readonly string directory;
    private readonly object gate = new();
    public VaultHeader Header { get; private set; }
    private Dictionary<string, PasswordRecord> conversationLocks = [];
    private WorkspaceVault(string directory, VaultHeader header, byte[] key)
    {
        this.directory = directory; Header = header; this.key = key;
        var path = Path.Combine(directory, "conversation-locks.enc");
        if (File.Exists(path)) conversationLocks = Codec.Decode<Dictionary<string, PasswordRecord>>(Unseal(File.ReadAllText(path)));
    }
    public static bool Exists(string directory) => File.Exists(Path.Combine(directory, "workspace.json"));
    public static VaultHeader ReadHeader(string directory)
    {
        var header = Codec.Decode<VaultHeader>(File.ReadAllText(Path.Combine(directory, "workspace.json")));
        if (header.Version != 1 || !Protocol.ValidId(header.WorkspaceId) || header.Iterations is < 600000 or > 2000000) throw new InvalidDataException("Thông tin workspace không hợp lệ.");
        return header;
    }
    public static WorkspaceVault Create(string directory, string password)
    {
        if (Exists(directory)) throw new InvalidOperationException("Workspace đã có mật khẩu.");
        if (password.Length < 8) throw new ArgumentException("Mật khẩu cần ít nhất 8 ký tự.");
        Directory.CreateDirectory(directory);
        var header = new VaultHeader { Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) };
        header.OwnerProof = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(header.OwnerSid + ":" + header.WorkspaceId), null, DataProtectionScope.CurrentUser));
        var vaultKey = RandomNumberGenerator.GetBytes(32); WrapKey(header, vaultKey, password);
        WriteHeader(directory, header); return new WorkspaceVault(directory, header, vaultKey);
    }
    public static WorkspaceVault Open(string directory, string password)
    {
        var header = ReadHeader(directory);
        if (!IsOwner(header)) throw new UnauthorizedAccessException("Workspace thuộc tài khoản Windows khác hoặc thông tin chủ sở hữu bị thay đổi.");
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(header.Salt), header.Iterations, HashAlgorithmName.SHA256, 32);
        try { return new WorkspaceVault(directory, header, Decrypt(derived, Convert.FromBase64String(header.WrappedKey), Encoding.UTF8.GetBytes(header.WorkspaceId + header.OwnerSid))); }
        catch (CryptographicException) { throw new UnauthorizedAccessException("Mật khẩu chưa đúng."); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
    public static bool IsOwner(VaultHeader header)
    {
        if (header.OwnerSid != WindowsIdentity.GetCurrent().User?.Value) return false;
        try
        {
            var proof = ProtectedData.Unprotect(Convert.FromBase64String(header.OwnerProof), null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(proof) == header.OwnerSid + ":" + header.WorkspaceId; }
            finally { CryptographicOperations.ZeroMemory(proof); }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return false; }
    }
    public bool VerifyAccountPassword(string password)
    {
        if (password.Length is < 8 or > 256) return false;
        lock (gate)
        {
            var header = ReadHeader(directory);
            var derived = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(header.Salt), header.Iterations, HashAlgorithmName.SHA256, 32);
            try
            {
                var actual = Decrypt(derived, Convert.FromBase64String(header.WrappedKey), Encoding.UTF8.GetBytes(header.WorkspaceId + header.OwnerSid));
                try { return CryptographicOperations.FixedTimeEquals(actual, key); }
                finally { CryptographicOperations.ZeroMemory(actual); }
            }
            catch (CryptographicException) { return false; }
            finally { CryptographicOperations.ZeroMemory(derived); }
        }
    }
    private static void WrapKey(VaultHeader header, byte[] key, string password)
    {
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(header.Salt), header.Iterations, HashAlgorithmName.SHA256, 32);
        try { header.WrappedKey = Convert.ToBase64String(Encrypt(derived, key, Encoding.UTF8.GetBytes(header.WorkspaceId + header.OwnerSid))); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
    private static void WriteHeader(string directory, VaultHeader header)
    {
        var file = Path.Combine(directory, "workspace.json"); File.WriteAllText(file + ".tmp", Codec.Encode(header)); File.Move(file + ".tmp", file, true);
    }
    public void ChangePassword(string current, string next)
    {
        if (next.Length < 8) throw new ArgumentException("Mật khẩu mới cần ít nhất 8 ký tự.");
        using var check = Open(directory, current);
        var header = Codec.Decode<VaultHeader>(Codec.Encode(Header)); header.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)); WrapKey(header, key, next); WriteHeader(directory, header); Header = header;
    }
    public string Seal(string plain) => "enc:v1:" + Convert.ToBase64String(Encrypt(key, Encoding.UTF8.GetBytes(plain), Encoding.UTF8.GetBytes(Header.WorkspaceId)));
    internal string PortableHeader(string password)
    {
        lock (gate)
        {
            var header = new VaultHeader { WorkspaceId = Header.WorkspaceId, OwnerSid = "", OwnerProof = "", Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) };
            WrapKey(header, key, password); return Codec.Encode(header);
        }
    }
    internal static WorkspaceVault RestorePortable(string directory, string json, string password)
    {
        var header = Codec.Decode<VaultHeader>(json);
        if (header.Version != 1 || !Protocol.ValidId(header.WorkspaceId) || header.Iterations != 600000 || header.OwnerSid != "") throw new InvalidDataException("Khóa sao lưu không hợp lệ.");
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(header.Salt), header.Iterations, HashAlgorithmName.SHA256, 32);
        byte[] restored;
        try { restored = Decrypt(derived, Convert.FromBase64String(header.WrappedKey), Encoding.UTF8.GetBytes(header.WorkspaceId)); }
        finally { CryptographicOperations.ZeroMemory(derived); }
        try
        {
            header.OwnerSid = WindowsIdentity.GetCurrent().User!.Value;
            header.OwnerProof = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(header.OwnerSid + ":" + header.WorkspaceId), null, DataProtectionScope.CurrentUser));
            header.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)); WrapKey(header, restored, password); WriteHeader(directory, header);
            return new WorkspaceVault(directory, header, restored);
        }
        catch { CryptographicOperations.ZeroMemory(restored); throw; }
    }
    public string Unseal(string value)
    {
        if (!value.StartsWith("enc:v1:", StringComparison.Ordinal)) return value;
        var plain = Decrypt(key, Convert.FromBase64String(value[7..]), Encoding.UTF8.GetBytes(Header.WorkspaceId));
        try { return Encoding.UTF8.GetString(plain); } finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static byte[] Encrypt(byte[] key, byte[] plain, byte[] aad)
    {
        var output = new byte[28 + plain.Length]; RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(key, 16); aes.Encrypt(output.AsSpan(0, 12), plain, output.AsSpan(28), output.AsSpan(12, 16), aad); return output;
    }
    private static byte[] Decrypt(byte[] key, byte[] cipher, byte[] aad)
    {
        if (cipher.Length < 28) throw new CryptographicException("Dữ liệu mã hóa không hợp lệ.");
        var output = new byte[cipher.Length - 28]; using var aes = new AesGcm(key, 16); aes.Decrypt(cipher.AsSpan(0, 12), cipher.AsSpan(28), cipher.AsSpan(12, 16), output, aad); return output;
    }
    public bool IsConversationLocked(string id) { lock (gate) return conversationLocks.ContainsKey(id); }
    public bool VerifyConversation(string id, string password) { lock (gate) return conversationLocks.TryGetValue(id, out var record) && record.Verify(password); }
    public void SetConversationPassword(string id, string password) { lock (gate) { conversationLocks[id] = PasswordRecord.Create(password); SaveLocks(); } }
    public void RemoveConversationPassword(string id) { lock (gate) { conversationLocks.Remove(id); SaveLocks(); } }
    private void SaveLocks()
    {
        var path = Path.Combine(directory, "conversation-locks.enc"); File.WriteAllText(path + ".tmp", Seal(Codec.Encode(conversationLocks))); File.Move(path + ".tmp", path, true);
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(key);
}

public static class ResetPolicy
{
    public static string Phrase(VaultHeader header) => "XOA " + header.WorkspaceId[..8].ToUpperInvariant();
    public static bool CanRequestVerification(VaultHeader header, string phrase) => WorkspaceVault.IsOwner(header) && string.Equals(phrase.Trim(), Phrase(header), StringComparison.Ordinal);
}
