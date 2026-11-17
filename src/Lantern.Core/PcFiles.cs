using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Lantern.Core;

/// <summary>Read-only access to explicitly shared local roots. No links, UNC paths, ADS or writes.</summary>
internal sealed class PcFiles : IDisposable
{
    private readonly TimeProvider clock;
    private readonly ITimer cleanup;
    private bool disposed;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Download> downloads = [];
    public PcFiles(TimeProvider clock)
    {
        this.clock = clock;
        cleanup = clock.CreateTimer(_ => ExpireIdle(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
    private void ExpireIdle()
    {
        if (!gate.Wait(0)) return;
        try { if (!disposed) Expire(); }
        finally { gate.Release(); }
    }
    private void Expire()
    {
        foreach (var key in downloads.Where(p => p.Value.Touched.AddMinutes(2) <= clock.GetUtcNow()).Select(p => p.Key).ToArray()) Close(key);
    }
    private sealed record Download(string Device, PcRoot Root, FileStream Stream, string Name, string Hash)
    { public DateTimeOffset Touched { get; set; } }
    public static string ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path[3..].Contains(':'))
            throw new ArgumentException("Chọn thư mục trên ổ đĩa cục bộ.");
        var full = System.IO.Path.GetFullPath(path);
        if (new DriveInfo(full[..3]).DriveType is not (DriveType.Fixed or DriveType.Removable)) throw new ArgumentException("Chỉ hỗ trợ ổ đĩa cục bộ.");
        return System.IO.Path.TrimEndingDirectorySeparator(full);
    }
    private static string Resolve(PcRoot root, string relative)
    {
        var basis = ValidateRoot(root.Path);
        if (relative.Contains(':') || relative.Contains('\\') || relative.StartsWith('/') || relative.Any(char.IsControl) || relative.Split('/').Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new ArgumentException("Đường dẫn không hợp lệ.");
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(basis, relative.Replace('/', '\\')));
        if (!full.Equals(basis, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(basis.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Ngoài thư mục được cấp.");
        return full;
    }
    public async Task<PcPacket> HandleAsync(string device, PcPacket request, Func<PcAccess> access, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Expire();
            if (request.Action is "files.read" or "files.close")
            {
                if (!downloads.TryGetValue(request.Ticket, out var download) || download.Device != device) throw new ArgumentException("Phiên tải đã hết hạn; chọn lại tệp.");
                if (!access().Roots.Any(r => r == download.Root)) { Close(request.Ticket); throw new UnauthorizedAccessException("Quyền đọc thư mục đã bị thu hồi."); }
                if (request.Action == "files.close") { Close(request.Ticket); return new(); }
                if (request.Offset < 0 || request.Offset > download.Stream.Length) throw new ArgumentException("Vị trí tải không hợp lệ.");
                download.Touched = clock.GetUtcNow(); download.Stream.Position = request.Offset;
                byte[] bytes = new byte[256 * 1024]; int count = await download.Stream.ReadAsync(bytes, ct);
                return new() { Data = Convert.ToBase64String(bytes, 0, count), Offset = request.Offset, Size = download.Stream.Length };
            }
            var root = access().Roots.Find(r => r.Id == request.Root) ?? throw new UnauthorizedAccessException("PC chưa cấp thư mục này.");
            var path = Resolve(root, request.Path);
            // Hold all ancestors without write/delete sharing throughout enumeration/open.
            // This prevents replacing a checked directory with a junction in the middle of an operation.
            using var guard = new DirectoryGuard(request.Action == "files.list" ? path : System.IO.Path.GetDirectoryName(path)!);
            if (request.Action == "files.list")
            {
                int skip = checked(request.Page * 100);
                var entries = new List<PcEntry>();
                int seen = 0;
                foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if ((item.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                    if (seen++ < skip) continue;
                    try { entries.Add(new(item.Name, (item.Attributes & FileAttributes.Directory) != 0, item is FileInfo file ? file.Length : 0, item.LastWriteTimeUtc)); }
                    catch (IOException) { continue; }
                    if (entries.Count == 101) break;
                }
                return new() { Entries = entries.Take(100).OrderByDescending(e => e.Directory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList(), More = entries.Count > 100, Page = request.Page };
            }
            if (request.Action != "files.open") throw new ArgumentException("Chức năng tệp không hợp lệ.");
            if (downloads.Count >= 8 || downloads.Values.Count(d => d.Device == device) >= 2) throw new InvalidOperationException("Đóng lượt tải trước rồi chọn tệp khác.");
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.System)) != 0) throw new UnauthorizedAccessException("Không tải thư mục, liên kết hoặc tệp hệ thống.");
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                VerifyHandle(stream.SafeFileHandle, path);
                if (stream.Length > MobileProtocol.MaxMobileFile) throw new ArgumentException("Tệp vượt giới hạn 100 MB.");
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)); stream.Position = 0;
                if (!access().Roots.Contains(root)) throw new UnauthorizedAccessException("Quyền thư mục đã thay đổi.");
                string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                downloads.Add(token, new(device, root, stream, System.IO.Path.GetFileName(path), hash) { Touched = clock.GetUtcNow() });
                return new() { Ticket = token, Name = System.IO.Path.GetFileName(path), Size = stream.Length, Hash = hash };
            }
            catch { await stream.DisposeAsync(); throw; }
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        { throw new InvalidOperationException("Không đọc được tệp/thư mục hoặc quyền đã bị thu hồi. Đóng tệp đang sửa và kiểm tra quyền trên PC.", ex); }
        finally { gate.Release(); }
    }
    private void Close(string token) { if (downloads.Remove(token, out var item)) item.Stream.Dispose(); }
    public void Dispose() { cleanup.Dispose(); gate.Wait(); try { disposed = true; foreach (var key in downloads.Keys.ToArray()) Close(key); } finally { gate.Release(); } }
    private sealed class DirectoryGuard : IDisposable
    {
        private readonly List<SafeFileHandle> handles = [];
        public DirectoryGuard(string path)
        {
            try
            {
                string current = path[..3];
                var parts = path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
                Open(current);
                foreach (var part in parts) { current = System.IO.Path.Combine(current, part); Open(current); }
            }
            catch { Dispose(); throw; }
        }
        private void Open(string path)
        {
            var handle = CreateFile(path, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            handles.Add(handle); VerifyHandle(handle, path);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Không duyệt liên kết thư mục.");
        }
        public void Dispose() { foreach (var handle in handles) handle.Dispose(); handles.Clear(); }
    }
    private static void VerifyHandle(SafeFileHandle handle, string expected)
    {
        var buffer = new StringBuilder(32768); uint count = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (count == 0 || count >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        var final = buffer.ToString(); if (final.StartsWith("\\\\?\\", StringComparison.Ordinal)) final = final[4..];
        if (!System.IO.Path.TrimEndingDirectorySeparator(final).Equals(System.IO.Path.TrimEndingDirectorySeparator(expected), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Đường dẫn thực đã thay đổi.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);
}
