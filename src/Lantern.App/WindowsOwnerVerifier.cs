using System.IO;
using System.Windows;
using System.Windows.Interop;
using Lantern.Core;
using Windows.Security.Credentials.UI;

namespace Lantern.App;

internal static class WindowsOwnerVerifier
{
    public static async Task<bool> VerifyAsync(Window owner, VaultHeader header, string reason)
    {
        if (!WorkspaceVault.IsOwner(header)) { MessageBox.Show(owner, "Chỉ tài khoản Windows đã tạo workspace này mới được xác nhận xóa.", "Không được phép"); return false; }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { MessageBox.Show(owner, "Luồng xóa có xác thực trong app cần Windows 11 và Windows Hello. Không có phương án bỏ qua xác thực.", "Không thể xác thực"); return false; }
        try
        {
            if (await UserConsentVerifier.CheckAvailabilityAsync() != UserConsentVerifierAvailability.Available)
            { MessageBox.Show(owner, "Windows Hello chưa sẵn sàng hoặc bị chính sách máy chặn. Workspace chưa bị thay đổi. Hãy thiết lập PIN/Windows Hello của tài khoản sở hữu trong Windows rồi thử lại.", "Chưa thể xác nhận chủ máy"); return false; }
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(new WindowInteropHelper(owner).EnsureHandle(), reason);
            if (result != UserConsentVerificationResult.Verified) { MessageBox.Show(owner, "Không xác thực được hoặc đã hủy. Dữ liệu được giữ nguyên.", "Chưa xóa dữ liệu"); return false; }
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(owner, "Windows Hello không khả dụng. Dữ liệu được giữ nguyên.", "Chưa xóa dữ liệu"); return false; }
    }
    // Called only after fresh Windows Hello approval by LoginWindow. Delete only app-owned paths.
    public static void DeleteWorkspaceFiles(string directory, string approvedWorkspaceId)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if (!WorkspaceVault.Exists(root) || !WorkspaceVault.IsOwner(WorkspaceVault.ReadHeader(root)) || WorkspaceVault.ReadHeader(root).WorkspaceId != approvedWorkspaceId || new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException("Workspace không hợp lệ để xóa.");
        var media = Path.Combine(root, "media");
        if (Directory.Exists(media)) DeleteOwnedTree(media, root);
        foreach (var name in new[] { "identity.bin", "history.db", "history.db-wal", "history.db-shm", "settings.json", "settings.json.tmp", "conversation-locks.enc", "conversation-locks.enc.tmp", "mobile-devices.enc", "mobile-devices.enc.tmp", "errors.log", "workspace.json.tmp", "workspace.json" }) File.Delete(Path.Combine(root, name));
    }
    private static void DeleteOwnedTree(string path, string root)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Từ chối xóa ngoài workspace.");
        var info = new DirectoryInfo(full);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) { Directory.Delete(full, false); return; }
        foreach (var file in info.GetFiles()) file.Delete();
        foreach (var sub in info.GetDirectories()) DeleteOwnedTree(sub.FullName, root);
        info.Delete();
    }
}
