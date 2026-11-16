using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lantern.Core;

public sealed record AvailableUpdate(Version Version, Uri DownloadUrl, string Sha256, long Size);

public sealed class ReleaseUpdater : IDisposable
{
    public const string Repository = "zeusato/Lantern";
    public const string AssetName = "Lantern-Windows-x64.zip";
    private readonly HttpClient http;
    public ReleaseUpdater(HttpMessageHandler? handler = null)
    {
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromMinutes(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Lantern-Update/1.1");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    public async Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseRelease(document.RootElement, current);
    }
    public static AvailableUpdate? ParseRelease(JsonElement release, Version current)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= current) return null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName) continue;
            var url = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "");
            if (url.Scheme != "https" || url.Host != "github.com" || !url.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("Nguồn cập nhật không hợp lệ.");
            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit)) throw new InvalidDataException("Bản phát hành chưa có SHA-256 hợp lệ. Hãy thử lại sau.");
            var size = asset.GetProperty("size").GetInt64();
            if (size is < 1 or > 512L * 1024 * 1024) throw new InvalidDataException("Kích thước bản cập nhật không hợp lệ.");
            return new AvailableUpdate(version, url, digest[7..], size);
        }
        return null;
    }
    public async Task<string> DownloadAsync(AvailableUpdate update, string cacheDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        var target = Path.Combine(Path.GetFullPath(cacheDirectory), $"Lantern-{update.Version}.zip");
        if (File.Exists(target) && await VerifyAsync(target, update, ct)) return target;
        var part = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0) { total += read; if (total > update.Size) throw new InvalidDataException("Gói cập nhật vượt quá kích thước công bố."); await output.WriteAsync(buffer.AsMemory(0, read), ct); }
            }
            if (!await VerifyAsync(part, update, ct)) throw new InvalidDataException("Gói cập nhật không khớp SHA-256.");
            File.Move(part, target, true); return target;
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }
    public static async Task<bool> VerifyAsync(string file, AvailableUpdate update, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(file);
        return stream.Length == update.Size && Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public static void ExtractPackage(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archive);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Lantern.exe", "HUONG-DAN.md", "THIRD-PARTY-NOTICES.md", "DOTNET-LICENSE.txt", "DOTNET-THIRD-PARTY-NOTICES.txt", "WPF-THIRD-PARTY-NOTICES.txt" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!zip.Entries.Any(e => e.FullName == "Lantern.exe")) throw new InvalidDataException("Gói cập nhật thiếu Lantern.exe.");
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            total += entry.Length;
            if (!allowed.Contains(entry.FullName) || !seen.Add(entry.FullName) || total > 1024L * 1024 * 1024 || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException("Nội dung gói cập nhật không hợp lệ.");
        }
        foreach (var entry in zip.Entries) entry.ExtractToFile(Path.Combine(destination, entry.FullName), false);
    }
    public void Dispose() => http.Dispose();
}
