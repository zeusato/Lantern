using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Lantern.Core;

namespace Lantern.App;

internal sealed class UpdateController : IDisposable
{
    public static Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new Version(1, 1, 0);
    public string Status { get; private set; } = "Chưa kiểm tra cập nhật.";
    public AvailableUpdate? Available { get; private set; }
    public string? DownloadedArchive { get; private set; }
    public event Action? Changed;
    public event Action? Ready;
    private readonly ReleaseUpdater updater = new();
    private bool busy;
    private string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanternUpdates");
    public async Task CheckAsync(CancellationToken ct)
    {
        if (busy) return;
        busy = true;
        try
        {
            Status = "Đang kiểm tra GitHub Releases…"; Changed?.Invoke();
            var release = await updater.CheckAsync(CurrentVersion, ct);
            if (release is null) { Status = "Đang dùng phiên bản mới nhất."; return; }
            if (DownloadedArchive is not null && Available?.Version == release.Version) { Status = $"Bản {release.Version} đã sẵn sàng cài đặt."; return; }
            Available = release; Status = $"Đang tải bản {release.Version}…"; Changed?.Invoke();
            DownloadedArchive = await updater.DownloadAsync(release, CacheDirectory, ct);
            Status = $"Bản {release.Version} đã tải xong và kiểm tra SHA-256."; Ready?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or TaskCanceledException or UnauthorizedAccessException)
        { Status = "Chưa cập nhật được: " + ex.Message; }
        finally { busy = false; Changed?.Invoke(); }
    }
    public async Task StartInstallAsync(CancellationToken ct)
    {
        if (DownloadedArchive is null || Available is null || !await ReleaseUpdater.VerifyAsync(DownloadedArchive, Available, ct)) throw new InvalidDataException("Cần tải lại gói cập nhật.");
        var executable = Environment.ProcessPath ?? throw new IOException("Không xác định được đường dẫn ứng dụng.");
        if (!Path.GetFileName(executable).Equals("Lantern.exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Cập nhật trực tiếp chỉ dành cho bản portable Lantern.exe.");
        var installDirectory = Path.GetDirectoryName(executable)!;
        var staging = Path.Combine(CacheDirectory, "install-" + Guid.NewGuid().ToString("N"));
        ReleaseUpdater.ExtractPackage(DownloadedArchive, staging);
        var sourceExe = Path.Combine(staging, "Lantern.exe");
        var fileVersion = FileVersionInfo.GetVersionInfo(sourceExe);
        if (fileVersion.FileMajorPart != Available.Version.Major || fileVersion.FileMinorPart != Available.Version.Minor || fileVersion.FileBuildPart != Available.Version.Build) throw new InvalidDataException("Phiên bản tệp không khớp bản phát hành.");
        // Fail before closing the app if the portable folder is read-only.
        var probe = Path.Combine(installDirectory, ".lantern-update-" + Guid.NewGuid().ToString("N"));
        using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        var script = Path.Combine(CacheDirectory, "apply-" + Guid.NewGuid().ToString("N") + ".ps1");
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lantern.App.UpdateInstaller.ps1")!;
        using (var reader = new StreamReader(resource, System.Text.Encoding.UTF8, true))
            await File.WriteAllTextAsync(script, await reader.ReadToEndAsync(ct), new System.Text.UTF8Encoding(true), ct);
        using var source = File.OpenRead(sourceExe); var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, ct));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-TargetPid", Environment.ProcessId.ToString(), "-SourceDirectory", staging, "-InstallDirectory", installDirectory, "-DataDirectory", App.DataDirectory, "-ExpectedExeHash", hash }) start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new IOException("Không mở được tiến trình cập nhật.");
    }
    public void Dispose() => updater.Dispose();
}
