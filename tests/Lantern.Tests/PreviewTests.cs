using System.Net;
using System.Net.Http.Headers;
using Lantern.Core;

internal static class PreviewTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        var metadata = LinkPreviews.Parse("<meta content='Thiết kế &amp; màu sắc' property='og:title'><meta property=og:image content='/cover.png'><meta content='Lantern' property='og:site_name'>", new Uri("https://example.com/page"));
        check(metadata.Title == "Thiết kế & màu sắc" && metadata.ImageUrl?.AbsoluteUri == "https://example.com/cover.png", "Open Graph parses reversed attributes, entities and relative images");
        check(LinkPreviews.Parse("<title>Fallback title</title>", new Uri("https://example.com/page")).Title == "Fallback title", "Link preview falls back to page title without Open Graph");
        check(!LinkPreviews.AllowedUri(new Uri("http://127.0.0.1/")) && !LinkPreviews.AllowedUri(new Uri("http://169.254.169.254/")) && !LinkPreviews.IsPublicAddress(IPAddress.Parse("::ffff:192.168.1.1")) && !LinkPreviews.AllowedUri(new Uri("https://name:secret@example.com/")), "Link previews reject local endpoints and embedded credentials");
        var handler = new PreviewHandler(); using var previews = new LinkPreviews(handler);
        var first = await previews.GetAsync(new Uri("https://example.com/page")); var second = await previews.GetAsync(new Uri("https://example.com/page"));
        check(first?.Title == "Sample title" && first.ImageBytes?.Length == 4 && ReferenceEquals(first, second) && handler.Requests == 2, "Link preview fetches image once and reuses cached metadata");
        check(await previews.GetAsync(new Uri("https://example.com/redirect")) is null && handler.LocalRequests == 0, "Redirects cannot make the preview fetch a private address");
        var message = new ChatMessage { Text = "Private body", SenderName = "Private sender" };
        var full = MessageNotifications.Create(message, "Private sender", "full", false);
        var hidden = MessageNotifications.Create(message, "Private sender", "private", false);
        var locked = MessageNotifications.Create(message, "Private sender", "full", true);
        check(full.Title == "Private sender" && full.Body == "Private body", "Full toast includes sender and message");
        check(!hidden.Title.Contains("Private") && !hidden.Body.Contains("Private") && hidden == locked, "Private or locked toast reveals neither sender nor message");
        check(Codec.Decode<AppSettings>(Codec.Encode(new AppSettings { MessageToasts = true, MessageToastMode = "private" })).MessageToastMode == "private", "Toast privacy choice persists in settings");
        check(!new AppSettings().CloseToTray && Codec.Decode<AppSettings>(Codec.Encode(new AppSettings { CloseToTray = true })).CloseToTray, "Close-to-tray is opt-in and persists in settings");
    }
    private sealed class PreviewHandler : HttpMessageHandler
    {
        internal int Requests, LocalRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; if (request.RequestUri!.Host == "127.0.0.1") LocalRequests++;
            if (request.RequestUri.AbsolutePath == "/redirect") { var redirect = new HttpResponseMessage(HttpStatusCode.Found); redirect.Headers.Location = new Uri("http://127.0.0.1/private"); return Task.FromResult(redirect); }
            HttpContent content = request.RequestUri.AbsolutePath == "/image.png" ? new ByteArrayContent([1, 2, 3, 4]) : new StringContent("<meta property='og:title' content='Sample title'><meta property='og:image' content='/image.png'>");
            content.Headers.ContentType = new MediaTypeHeaderValue(request.RequestUri.AbsolutePath == "/image.png" ? "image/png" : "text/html"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
