using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Lantern.Core;

// A versioned, password-encrypted archive. Bounded authenticated chunks keep large
// media files off the managed heap; an authenticated final record detects truncation.
public static class WorkspaceBackup
{
    private const int ChunkSize = 1024 * 1024;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LANTERNBACKUP1");
    public static void Export(string destination, string password, Identity identity, WorkspaceVault vault, LocalStore store, CancellationToken ct = default)
    {
        if (password.Length < 12) throw new ArgumentException("Mật khẩu sao lưu cần ít nhất 12 ký tự.");
        var staging = Path.Combine(store.DirectoryPath, ".backup-" + Guid.NewGuid().ToString("N"));
        var output = Path.GetFullPath(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            var snapshot = Path.Combine(staging, "profile"); Directory.CreateDirectory(snapshot);
            store.Snapshot(Path.Combine(snapshot, "history.db"));
            File.WriteAllText(Path.Combine(snapshot, "portable.json"), vault.PortableHeader(password));
            identity.Export(Path.Combine(snapshot, "identity.lanternkey"), password);
            foreach (var name in new[] { "settings.json", "conversation-locks.enc" })
                if (File.Exists(Path.Combine(store.DirectoryPath, name))) File.Copy(Path.Combine(store.DirectoryPath, name), Path.Combine(snapshot, name));
            CopyDirectory(Path.Combine(store.DirectoryPath, "stickers"), Path.Combine(snapshot, "stickers"), ct);
            using (var copy = new LocalStore(snapshot, vault))
            {
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var message in copy.Messages(limit: int.MaxValue))
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(message.FilePath)) continue;
                    var source = message.FilePath;
                    // A transfer still in progress is resumed from the sender after migration.
                    if (!File.Exists(source))
                    {
                        message.FilePath = "media/" + Guid.NewGuid().ToString("N") + Path.GetExtension(source);
                        message.Transferred = 0;
                        if (message.Outgoing) { message.Status = "error"; message.Error = "Tệp gốc không còn trên máy khi sao lưu. Hãy gửi lại tệp."; }
                        else if (message.Status != "declined") { message.Status = "offered"; message.Error = ""; }
                        copy.SaveMessage(message); continue;
                    }
                    if (!files.TryGetValue(source, out var relative))
                    {
                        relative = "media/" + Guid.NewGuid().ToString("N") + Path.GetExtension(source);
                        CopyFile(source, Path.Combine(snapshot, relative), ct); files.Add(source, relative);
                    }
                    message.FilePath = relative; copy.SaveMessage(message);
                }
            }
            var archive = Path.Combine(staging, "data.zip");
            ZipFile.CreateFromDirectory(snapshot, archive, CompressionLevel.Fastest, false);
            using (var input = File.OpenRead(archive))
            using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) Transform(input, target, password, true, ct);
            ct.ThrowIfCancellationRequested(); File.Move(output, destination, true);
        }
        finally { File.Delete(output); Directory.Delete(staging, true); }
    }

    public static bool CanRestore(string directory) => !new[] { "workspace.json", "history.db", "identity.bin", "media", "stickers" }.Any(name => File.Exists(Path.Combine(directory, name)) || Directory.Exists(Path.Combine(directory, name)));

    public static void Restore(string source, string password, string destination, CancellationToken ct = default)
    {
        if (!CanRestore(destination)) throw new InvalidOperationException("Chỉ khôi phục vào workspace mới, chưa có dữ liệu.");
        Directory.CreateDirectory(destination);
        var staging = Path.Combine(destination, ".restore-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        var moved = new List<string>(); bool committed = false;
        try
        {
            var archive = Path.Combine(staging, "data.zip");
            using (var input = File.OpenRead(source))
            using (var target = File.Create(archive)) Transform(input, target, password, false, ct);
            var profile = Path.Combine(staging, "profile"); Directory.CreateDirectory(profile);
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/');
                    if (!(name is "portable.json" or "identity.lanternkey" or "history.db" or "settings.json" or "conversation-locks.enc" || name.StartsWith("media/") || name.StartsWith("stickers/"))) throw new InvalidDataException("Nội dung sao lưu không hợp lệ.");
                    var path = SafePath(profile, name);
                    if (name.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew); Copy(input, output, ct);
                }
            }
            if (!File.Exists(Path.Combine(profile, "history.db"))) throw new InvalidDataException("Bản sao lưu thiếu lịch sử.");
            if (File.Exists(Path.Combine(profile, "settings.json"))) _ = Codec.Decode<AppSettings>(File.ReadAllText(Path.Combine(profile, "settings.json")));
            using (var vault = WorkspaceVault.RestorePortable(profile, File.ReadAllText(Path.Combine(profile, "portable.json")), password))
            {
                Identity.Import(Path.Combine(profile, "identity.lanternkey"), password, profile, vault);
                using var store = new LocalStore(profile, vault);
                foreach (var message in store.Messages(limit: int.MaxValue))
                {
                    ct.ThrowIfCancellationRequested();
                    if (message.FilePath.Length == 0) continue;
                    var relative = message.FilePath.Replace('\\', '/');
                    if (!relative.StartsWith("media/")) throw new InvalidDataException("Tệp đính kèm trong bản sao lưu không hợp lệ.");
                    var attachment = SafePath(profile, relative);
                    if (File.Exists(attachment))
                    {
                        try { File.WriteAllText(attachment + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); } catch (IOException) { }
                    }
                    else if (message.Status is not ("offered" or "declined" or "error")) throw new InvalidDataException("Bản sao lưu thiếu tệp đính kèm đã tải.");
                    message.FilePath = SafePath(destination, relative); store.SaveMessage(message);
                }
                // Validate encrypted records before installing any restored data.
                _ = store.Peers(); _ = store.Groups();
            }
            File.Delete(Path.Combine(profile, "portable.json")); File.Delete(Path.Combine(profile, "identity.lanternkey"));
            ct.ThrowIfCancellationRequested();
            if (!CanRestore(destination)) throw new InvalidOperationException("Workspace đã có dữ liệu; chưa khôi phục.");
            foreach (var path in Directory.EnumerateFileSystemEntries(profile).OrderBy(p => Path.GetFileName(p) == "workspace.json" ? 1 : 0))
            {
                var target = Path.Combine(destination, Path.GetFileName(path));
                if (Directory.Exists(path)) Directory.Move(path, target); else File.Move(path, target, true);
                moved.Add(target);
            }
            committed = true;
        }
        finally
        {
            if (!committed) foreach (var path in moved) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
            Directory.Delete(staging, true);
        }
    }

    private static string SafePath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || relative.Contains(':') || !path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Đường dẫn sao lưu không hợp lệ.");
        return path;
    }
    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            if (!file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) CopyFile(file, Path.Combine(destination, Path.GetRelativePath(source, file)), ct);
    }
    private static void CopyFile(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read); using var output = File.Create(destination); Copy(input, output, ct);
    }
    private static void Copy(Stream input, Stream output, CancellationToken ct)
    {
        byte[] buffer = new byte[ChunkSize]; int count;
        while ((count = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
    }
    private static void Transform(Stream input, Stream output, string password, bool encrypt, CancellationToken ct)
    {
        var salt = new byte[24]; var noncePrefix = new byte[8];
        if (encrypt) { RandomNumberGenerator.Fill(salt); RandomNumberGenerator.Fill(noncePrefix); output.Write(Magic); output.Write(salt); output.Write(noncePrefix); }
        else { var magic = new byte[Magic.Length]; input.ReadExactly(magic); if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("Không phải bản sao lưu Lantern hợp lệ."); input.ReadExactly(salt); input.ReadExactly(noncePrefix); }
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16); byte[] plain = new byte[ChunkSize], cipher = new byte[ChunkSize], tag = new byte[16], nonce = new byte[12], length = new byte[4]; noncePrefix.CopyTo(nonce, 0);
            for (uint index = 0; ; index = checked(index + 1))
            {
                ct.ThrowIfCancellationRequested(); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), index);
                if (encrypt)
                {
                    int count = input.ReadAtLeast(plain, ChunkSize, false); System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, count);
                    aes.Encrypt(nonce, plain.AsSpan(0, count), cipher.AsSpan(0, count), tag, length); output.Write(length); output.Write(tag); output.Write(cipher, 0, count); if (count == 0) break;
                }
                else
                {
                    input.ReadExactly(length); int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(length);
                    if (count is < 0 or > ChunkSize) throw new InvalidDataException("Bản sao lưu bị hỏng.");
                    input.ReadExactly(tag); input.ReadExactly(cipher.AsSpan(0, count)); aes.Decrypt(nonce, cipher.AsSpan(0, count), tag, plain.AsSpan(0, count), length);
                    if (count == 0) { if (input.ReadByte() != -1) throw new InvalidDataException("Bản sao lưu có dữ liệu thừa."); break; }
                    output.Write(plain, 0, count);
                }
            }
            CryptographicOperations.ZeroMemory(plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
