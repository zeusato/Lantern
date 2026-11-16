using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Lantern.Core;

public sealed record LinkPreview(string Title, string SiteName, Uri? ImageUrl, byte[]? ImageBytes = null);

public sealed class LinkPreviews : IDisposable
{
    private readonly HttpClient http;
    private readonly SemaphoreSlim parallel = new(3, 3);
    private readonly ConcurrentDictionary<string, Task<LinkPreview?>> cache = new();
    private readonly CancellationTokenSource lifetime = new();
    public LinkPreviews(HttpMessageHandler? handler = null)
    {
        if (handler is null)
        {
            var sockets = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            sockets.ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                foreach (var address in addresses.Where(IsPublicAddress))
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try { await socket.ConnectAsync(address, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, true); }
                    catch { socket.Dispose(); if (ct.IsCancellationRequested) throw; }
                }
                throw new HttpRequestException("No public endpoint for preview.");
            };
            handler = sockets;
        }
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(7) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Lantern-LinkPreview/1.1");
    }
    public Task<LinkPreview?> GetAsync(Uri uri)
    {
        if (!AllowedUri(uri)) return Task.FromResult<LinkPreview?>(null);
        if (cache.Count > 256) cache.Clear();
        return cache.GetOrAdd(uri.AbsoluteUri, _ => FetchAsync(uri));
    }
    private async Task<LinkPreview?> FetchAsync(Uri uri)
    {
        try { await parallel.WaitAsync(lifetime.Token); } catch (OperationCanceledException) { return null; }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(7));
            var page = await ReadAsync(uri, 512 * 1024, false, timeout.Token); if (page is null) return null;
            var preview = Parse(Encoding.UTF8.GetString(page.Value.Bytes), page.Value.FinalUri);
            if (preview.ImageUrl is not null)
            {
                try { var image = await ReadAsync(preview.ImageUrl, 3 * 1024 * 1024, true, timeout.Token); if (image is not null) preview = preview with { ImageBytes = image.Value.Bytes }; }
                catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) { }
            }
            return preview;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or RegexMatchTimeoutException or ArgumentException or ObjectDisposedException) { return null; }
        finally { parallel.Release(); }
    }
    private async Task<(byte[] Bytes, Uri FinalUri)?> ReadAsync(Uri uri, int limit, bool image, CancellationToken ct)
    {
        for (int redirect = 0; redirect < 4; redirect++)
        {
            if (!AllowedUri(uri)) return null;
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location) { uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue; }
            if (!response.IsSuccessStatusCode) return null;
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (image ? !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || type.Contains("svg", StringComparison.OrdinalIgnoreCase) : type is not ("text/html" or "application/xhtml+xml")) return null;
            if (image && response.Content.Headers.ContentLength > limit) return null;
            await using var input = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var buffer = new byte[8192];
            while (output.Length <= limit)
            {
                var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)output.Length)), ct);
                if (count == 0) break; output.Write(buffer, 0, count);
            }
            if (image && output.Length > limit) return null;
            return (output.ToArray().Take(limit).ToArray(), uri);
        }
        return null;
    }
    public static LinkPreview Parse(string html, Uri page)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in Regex.Matches(html, @"<meta\b[^>]{0,4096}>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150)))
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attr in Regex.Matches(tag.Value, "(?<key>[\\w:-]+)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.None, TimeSpan.FromMilliseconds(150))) attrs[attr.Groups["key"].Value] = WebUtility.HtmlDecode(attr.Groups["value"].Value);
            var key = attrs.GetValueOrDefault("property") ?? attrs.GetValueOrDefault("name");
            if (key is not null && attrs.TryGetValue("content", out var value)) values.TryAdd(key, value);
        }
        var title = values.GetValueOrDefault("og:title") ?? values.GetValueOrDefault("twitter:title") ?? WebUtility.HtmlDecode(Regex.Match(html, @"<title\b[^>]*>([^<]{0,1000})</title>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150)).Groups[1].Value);
        var image = values.GetValueOrDefault("og:image:secure_url") ?? values.GetValueOrDefault("og:image") ?? values.GetValueOrDefault("og:image:url") ?? values.GetValueOrDefault("twitter:image");
        Uri? imageUrl = image is not null && Uri.TryCreate(page, image, out var parsed) && AllowedUri(parsed) ? parsed : null;
        return new LinkPreview(Clean(title, 180), Clean(values.GetValueOrDefault("og:site_name") ?? page.Host, 80), imageUrl);
    }
    private static string Clean(string value, int max)
    {
        var text = string.Concat(WebUtility.HtmlDecode(value).Where(c => !char.IsControl(c))).Trim(); return text.Length > max ? text[..max] + "…" : text;
    }
    public static bool AllowedUri(Uri uri) => uri.IsAbsoluteUri && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Port is 80 or 443 && uri.Host.Contains('.') && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) || IsPublicAddress(ip));
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4(); if (IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return (bytes[0] & 0xe0) == 0x20;
        return bytes[0] is > 0 and < 224 && bytes[0] != 10 && bytes[0] != 127 && !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) && !(bytes[0] == 192 && bytes[1] == 168) && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) && !(bytes[0] == 198 && bytes[1] is 18 or 19);
    }
    public void Dispose() { lifetime.Cancel(); http.Dispose(); }
}
