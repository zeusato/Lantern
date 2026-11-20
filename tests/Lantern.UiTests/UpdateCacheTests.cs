using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Lantern.App;
using Lantern.Core;

internal sealed partial class UiHarness
{
    private async Task RunUpdateCache()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.Combine(Path.GetTempPath(), "Lantern-update-cache-" + Guid.NewGuid().ToString("N"));
        var handler = new UpdateCacheHandler();
        try
        {
            string archive;
            using (var updates = new UpdateController(new ReleaseUpdater(handler), root))
            {
                int ready = 0; updates.Ready += () => ready++;
                await updates.CheckAsync(CancellationToken.None); archive = updates.DownloadedArchive!;
                Check(ready == 1 && updates.Available?.Version == new Version(9, 0, 0) && File.Exists(archive), "Update notification fires only after a verified download has completed");
                await updates.CheckAsync(CancellationToken.None);
                Check(ready == 1 && handler.Downloads == 1, "Repeated periodic checks reuse the installer and do not repeat the ready notification");
                handler.Version = "10.0.0"; handler.FailDownload = true;
                await updates.CheckAsync(CancellationToken.None);
                Check(updates.Available?.Version == new Version(9, 0, 0) && updates.DownloadedArchive == archive && File.Exists(archive), "Failure downloading a newer release retains the previous ready installer and matching metadata");
            }
            var offline = new UpdateCacheHandler { Offline = true };
            using (var reopened = new UpdateController(new ReleaseUpdater(offline), root))
            {
                int ready = 0; reopened.Ready += () => ready++;
                await reopened.RestoreAsync(CancellationToken.None);
                Check(reopened.DownloadedArchive == archive && reopened.Available?.Version == new Version(9, 0, 0) && offline.Requests == 0, "Ignoring an update and restarting offline preserves a verified installer ready for manual installation");
                Check(ready == 0, "Restoring a deferred update does not repeatedly prompt or start installation");
            }
            await File.WriteAllTextAsync(archive, "corrupted installer");
            using var corrupt = new UpdateController(new ReleaseUpdater(new UpdateCacheHandler { Offline = true }), root);
            await corrupt.RestoreAsync(CancellationToken.None);
            Check(corrupt.DownloadedArchive is null, "A corrupted cached installer is never offered for installation");
            Console.WriteLine($"{passed} update cache checks passed.");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
        finally { Shutdown(Environment.ExitCode); }
    }
    private sealed class UpdateCacheHandler : HttpMessageHandler
    {
        public int Requests, Downloads;
        public string Version = "9.0.0";
        public bool FailDownload, Offline;
        private readonly byte[] archive = Encoding.UTF8.GetBytes("Synthetic installer fixture; never executed");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++; if (Offline) throw new HttpRequestException("Offline fixture");
            if (request.RequestUri!.Host == "api.github.com")
            {
                var metadata = JsonSerializer.Serialize(new { draft = false, prerelease = false, tag_name = "v" + Version, assets = new[] { new { name = ReleaseUpdater.AssetName, browser_download_url = "https://github.com/zeusato/Lantern/releases/download/v" + Version + "/" + ReleaseUpdater.AssetName, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(archive)), size = archive.Length } } });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(metadata) });
            }
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(FailDownload ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }
    }
}
