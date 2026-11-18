using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Lantern.Core;

namespace Lantern.App;

internal sealed class HtmlPreview : Window
{
    internal const long MaxPreviewBytes = 16 * 1024 * 1024;
    internal static bool Supports(ChatMessage message) => message.Kind == "file" && Path.GetExtension(message.FileName).ToLowerInvariant() is ".html" or ".htm";
    private readonly WebView2 browser = new();
    private readonly TextBlock status = new() { Text = "Đang chuẩn bị…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
    private readonly CheckBox internet = new() { Content = "Tải tài nguyên Internet", Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
    private readonly CancellationTokenSource lifetime = new();
    private readonly string profilePath = Path.Combine(Path.GetTempPath(), "Lantern-html-" + Guid.NewGuid().ToString("N"));
    private readonly string documentUrl = "https://preview.lantern.invalid/" + Guid.NewGuid().ToString("N") + "/index.html";
    private byte[]? html;
    internal WebView2 Browser => browser;
    internal bool Ready { get; private set; }

    internal HtmlPreview(Window owner, string name)
    {
        Owner = owner; Title = name; Width = 1000; Height = 720; MinWidth = 480; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel(); Content = layout;
        var header = new DockPanel(); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        DockPanel.SetDock(internet, Dock.Right); header.Children.Add(internet); header.Children.Add(status); layout.Children.Add(browser);
        internet.IsEnabled = false;
        internet.Click += (_, _) => { if (Ready) browser.Reload(); };
        Closed += (_, _) => { lifetime.Cancel(); browser.Dispose(); CleanupProfile(); };
    }

    internal async Task LoadAsync(Func<CancellationToken, Action<string>, Task<string>> receive)
    {
        try
        {
            var path = await receive(lifetime.Token, text => status.Text = text);
            lifetime.Token.ThrowIfCancellationRequested();
            if (new FileInfo(path).Length > MaxPreviewBytes) throw new IOException("Tệp HTML quá lớn để xem trước (tối đa 16 MB).");
            html = await File.ReadAllBytesAsync(path, lifetime.Token);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profilePath);
            environment.BrowserProcessExited += (_, _) => CleanupProfile();
            lifetime.Token.ThrowIfCancellationRequested();
            var options = environment.CreateCoreWebView2ControllerOptions(); options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options);
            lifetime.Token.ThrowIfCancellationRequested();
            var core = browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false; core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) => { if (!IsDocument(e.Uri)) e.Cancel = true; };
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (IsDocument(e.Request.Uri))
                {
                    var remote = internet.IsChecked == true ? " https: http:" : "";
                    // Serve only this attachment, never map its containing directory.
                    var policy = "default-src 'none'; script-src 'unsafe-inline' 'unsafe-eval' data: blob:" + remote + "; style-src 'unsafe-inline' data:" + remote + "; img-src data: blob:" + remote + "; font-src data:" + remote + "; media-src data: blob:" + remote + "; connect-src " + (remote.Length == 0 ? "'none'" : "https: http:") + "; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; sandbox allow-scripts allow-same-origin";
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(html, false), 200, "OK", "Content-Type: text/html\r\nCache-Control: no-store\r\nContent-Security-Policy: " + policy);
                }
                else if (!AllowsResource(e.Request.Uri, internet.IsChecked == true))
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(), 403, "Forbidden", "Content-Type: text/plain");
            };
            core.NavigationCompleted += (_, e) =>
            {
                Ready = e.IsSuccess;
                status.Text = e.IsSuccess ? "HTML · Chạy trong khung xem" : "Không mở được bản xem trước. Có thể lưu tệp để mở riêng.";
                internet.IsEnabled = true;
            };
            core.Navigate(documentUrl);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
                status.Text = ex is WebView2RuntimeNotFoundException ? "Cần Microsoft Edge WebView2 Runtime để xem HTML trong Lantern." : "Chưa mở được HTML: " + ex.Message;
        }
    }

    private bool IsDocument(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.GetLeftPart(UriPartial.Path) == documentUrl;
    internal static bool AllowsResource(string address, bool allowInternet) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        (uri.Scheme is "data" or "blob" || allowInternet && uri.Scheme is "http" or "https");

    private void CleanupProfile()
    {
        // The only deletable directory is the unique preview profile created above.
        var full = Path.GetFullPath(profilePath);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Lantern-html-", StringComparison.Ordinal)) return;
        try { if (Directory.Exists(full)) Directory.Delete(full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public partial class MainWindow
{
    private readonly Dictionary<string, HtmlPreview> htmlPreviews = [];
    private async Task OpenHtmlAsync(ChatMessage message)
    {
        if (appLocking || ConversationProtected || !HtmlPreview.Supports(message)) return;
        if (htmlPreviews.TryGetValue(message.Id, out var existing)) { existing.Activate(); return; }
        if (message.FileSize > HtmlPreview.MaxPreviewBytes) { ShowToast("Tệp HTML quá lớn để xem trước (tối đa 16 MB). Có thể nhận và lưu tệp."); return; }
        var preview = new HtmlPreview(this, message.FileName); htmlPreviews[message.Id] = preview; mediaWindows.Add(preview);
        ProtectOwnedWindow(preview, message.ConversationId);
        preview.Closed += (_, _) => { htmlPreviews.Remove(message.Id); mediaWindows.Remove(preview); };
        preview.Show();
        await preview.LoadAsync(async (ct, report) =>
        {
            var current = store.Message(message.Id) ?? throw new IOException("Tệp không còn trong hội thoại.");
            if (!File.Exists(current.FilePath))
            {
                if (current.Outgoing) throw new IOException("Bản tệp đã gửi không còn trên máy.");
                report("Đang tải HTML…"); await node.AcceptFileAsync(current.Id);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    current = store.Message(message.Id) ?? throw new IOException("Tệp đã bị xóa hoặc hết hạn.");
                    if (current.Status == "received" && File.Exists(current.FilePath)) break;
                    if (current.Status is "error" or "declined") throw new IOException(current.Error.Length > 0 ? current.Error : "Tải tệp đã dừng.");
                    if (!node.IsConnected(current.SenderId)) throw new IOException("Người gửi đã ngắt kết nối. Bấm Mở HTML để tiếp tục khi họ online.");
                    var downloaded = File.Exists(current.FilePath + ".part") ? new FileInfo(current.FilePath + ".part").Length : 0;
                    report($"Đang tải HTML… {FormatBytes(downloaded)} / {FormatBytes(current.FileSize)}");
                    await Task.Delay(150, ct);
                }
            }
            ct.ThrowIfCancellationRequested();
            return current.FilePath;
        });
    }
}
