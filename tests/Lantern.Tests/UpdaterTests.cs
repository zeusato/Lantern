using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lantern.Core;

internal static class UpdaterTests
{
    public static void VerifyReleasePackage(string archive, string licensePath)
    {
        var destination = Path.Combine(Path.GetTempPath(), "Lantern-release-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            // This is the immutable contract of already installed updaters.
            string[] legacyNames = ["Lantern.exe", "HUONG-DAN.md", "THIRD-PARTY-NOTICES.md", "DOTNET-LICENSE.txt", "DOTNET-THIRD-PARTY-NOTICES.txt", "WPF-THIRD-PARTY-NOTICES.txt"];
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count != legacyNames.Length || !legacyNames.ToHashSet(StringComparer.Ordinal).SetEquals(zip.Entries.Select(e => e.FullName)))
                    throw new InvalidDataException("Release package is incompatible with installed updaters.");
            }
            ReleaseUpdater.ExtractPackage(archive, destination);
            if (!File.ReadAllText(Path.Combine(destination, "THIRD-PARTY-NOTICES.md")).Contains(File.ReadAllText(licensePath), StringComparison.Ordinal))
                throw new InvalidDataException("Release package is missing the full WebView2 SDK license.");
            Console.WriteLine("PASS: Actual release archive extracts through the updater, retains legacy filenames and includes the full WebView2 SDK license.");
        }
        finally { if (Directory.Exists(destination)) Directory.Delete(destination, true); }
    }

    public static async Task Run(string root, Action<bool, string> check)
    {
        var zipFile = Path.Combine(root, "update-fixture.zip");
        using (var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("Lantern.exe").Open()); writer.Write("Synthetic test fixture, not executable."); }
        var bytes = await File.ReadAllBytesAsync(zipFile); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var metadata = JsonSerializer.Serialize(new { draft = false, prerelease = false, tag_name = "v9.0.0", assets = new[] { new { name = ReleaseUpdater.AssetName, browser_download_url = "https://github.com/zeusato/Lantern/releases/download/v9.0.0/" + ReleaseUpdater.AssetName, digest = "sha256:" + hash, size = bytes.Length } } });
        using var updater = new ReleaseUpdater(new FakeHandler(metadata, bytes));
        var update = await updater.CheckAsync(new Version(1, 0, 0));
        check(update?.Version == new Version(9, 0, 0) && await updater.CheckAsync(new Version(9, 0, 0)) is null, "Updater accepts only a newer stable release");
        var cache = Path.Combine(root, "update-cache"); var archive = await updater.DownloadAsync(update!, cache);
        check(await ReleaseUpdater.VerifyAsync(archive, update!), "Downloaded update size and SHA-256 match GitHub metadata");
        ReleaseUpdater.ExtractPackage(archive, Path.Combine(root, "update-extracted"));
        check(File.Exists(Path.Combine(root, "update-extracted", "Lantern.exe")), "Update package extracts expected files");
        var rejected = false;
        try { await updater.DownloadAsync(update! with { Sha256 = new string('0', 64) }, Path.Combine(root, "invalid-update")); } catch (InvalidDataException) { rejected = true; }
        check(rejected && !Directory.EnumerateFiles(Path.Combine(root, "invalid-update")).Any(), "Checksum mismatch rejects update and cleans partial files");
        using var external = JsonDocument.Parse(metadata.Replace("github.com/zeusato/Lantern/", "evil.example/zeusato/Lantern/"));
        rejected = false; try { ReleaseUpdater.ParseRelease(external.RootElement, new Version(1, 0)); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Updater rejects release assets outside the configured GitHub repository");
        var traversal = Path.Combine(root, "update-traversal.zip");
        using (var zip = ZipFile.Open(traversal, ZipArchiveMode.Create)) { zip.CreateEntry("Lantern.exe"); zip.CreateEntry("../outside.txt"); }
        rejected = false; try { ReleaseUpdater.ExtractPackage(traversal, Path.Combine(root, "unsafe-extract")); } catch (InvalidDataException) { rejected = true; }
        check(rejected && !File.Exists(Path.Combine(root, "outside.txt")) && !Directory.EnumerateFiles(Path.Combine(root, "unsafe-extract")).Any(), "Zip traversal is rejected before extracting any file");
    }
    private sealed class FakeHandler(string metadata, byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.Host == "api.github.com" ? new StringContent(metadata, Encoding.UTF8, "application/json") : new ByteArrayContent(archive) });
    }
}
