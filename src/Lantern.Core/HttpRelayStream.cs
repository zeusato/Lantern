using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace Lantern.Core;

// This stream transports TLS records. Application JSON is never sent to the relay directly.
public sealed class RelayApi : IDisposable
{
    private readonly HttpClient http;
    private readonly byte[] registrationKey;
    public RelayApi(RelayConfiguration config)
    {
        if (!config.Enabled) throw new ArgumentException("Relay chưa được cấu hình.");
        registrationKey = Encoding.UTF8.GetBytes(config.ServiceToken);
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(config.Url), Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.Add("OAI-Sites-Authorization", "Bearer " + config.ServiceToken);
    }
    public async Task<JsonElement> Call(string path, string key, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }
    public Task<JsonElement> Register(MobileRoute route, CancellationToken ct)
    {
        long issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes(string.Join("\n", "Lantern-route-v1", route.Id, route.HostKey, route.ClientKey, issuedAt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var signature = Convert.ToHexString(HMACSHA256.HashData(registrationKey, payload)).ToLowerInvariant();
        return Call("v1/routes", route.HostKey, new { id = route.Id, clientKey = route.ClientKey, registration = new { issuedAt, signature } }, ct);
    }
    public void Dispose() { http.Dispose(); CryptographicOperations.ZeroMemory(registrationKey); }
}

public sealed class HttpRelayStream : Stream
{
    private readonly Channel<byte[]> incoming = Channel.CreateBounded<byte[]>(32);
    private readonly Channel<byte[]> outgoing = Channel.CreateBounded<byte[]>(32);
    private readonly CancellationTokenSource life;
    private readonly Task pump;
    private byte[] current = []; private int position;
    private readonly RelayApi api; private readonly string path, key, session;
    private long received;
    public HttpRelayStream(RelayApi api, MobileRoute route, string session, bool host, CancellationToken ct)
    {
        this.api = api; this.session = session; key = host ? route.HostKey : route.ClientKey; path = "v1/routes/" + route.Id + "/exchange";
        life = CancellationTokenSource.CreateLinkedTokenSource(ct); pump = Task.WhenAll(Receive(), Send());
    }
    private async Task Receive()
    {
        Exception? failure = null;
        try
        {
            var lastSuccess = DateTime.UtcNow;
            while (!life.IsCancellationRequested)
            {
                JsonElement reply;
                try
                {
                    reply = await api.Call(path, key, new { session, after = Interlocked.Read(ref received), batch = 4 }, life.Token);
                    lastSuccess = DateTime.UtcNow;
                }
                catch (Exception ex) when ((ex is HttpRequestException { StatusCode: null } or HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } or TaskCanceledException) && !life.IsCancellationRequested && DateTime.UtcNow - lastSuccess < TimeSpan.FromSeconds(45))
                { await Task.Delay(700, life.Token); continue; }
                if (reply.TryGetProperty("packets", out var packets) && packets.GetArrayLength() > 0)
                {
                    foreach (var packet in packets.EnumerateArray()) {
                    var next = packet.GetProperty("seq").GetInt64(); if (next != Interlocked.Read(ref received) + 1) throw new IOException("Relay mất thứ tự gói tin.");
                    var bytes = Convert.FromBase64String(packet.GetProperty("data").GetString()!);
                    if (bytes.Length is < 1 or > 49152) throw new IOException("Gói relay không hợp lệ.");
                    await incoming.Writer.WriteAsync(bytes, life.Token); Interlocked.Exchange(ref received, next); }
                }
                else await Task.Delay(50, life.Token);
            }
        }
        catch (Exception ex) { failure = ex; }
        finally { incoming.Writer.TryComplete(failure); outgoing.Writer.TryComplete(failure); life.Cancel(); }
    }
    private async Task Send()
    {
        Exception? failure = null;
        try
        {
            long seq = 0;
            while (!life.IsCancellationRequested)
            {
                var first = await outgoing.Reader.ReadAsync(life.Token);
                using var bytes = new MemoryStream(); bytes.Write(first);
                while (outgoing.Reader.TryPeek(out var next) && bytes.Length + next.Length <= 49152 && outgoing.Reader.TryRead(out next)) bytes.Write(next);
                var data = Convert.ToBase64String(bytes.ToArray()); seq++; var started = DateTime.UtcNow;
                while (true) try { await api.Call(path, key, new { session, after = Interlocked.Read(ref received), seq, data, pushOnly = true }, life.Token); break; }
                catch (Exception ex) when ((ex is HttpRequestException { StatusCode: null } or HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } or TaskCanceledException) && !life.IsCancellationRequested && DateTime.UtcNow - started < TimeSpan.FromSeconds(45)) { await Task.Delay(150, life.Token); }
            }
        }
        catch (Exception ex) { failure = ex; }
        finally { incoming.Writer.TryComplete(failure); outgoing.Writer.TryComplete(failure); life.Cancel(); }
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        if (position == current.Length) { current = await incoming.Reader.ReadAsync(cancellationToken); position = 0; }
        var count = Math.Min(buffer.Length, current.Length - position); current.AsMemory(position, count).CopyTo(buffer); position += count; return count;
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (buffer.Length > 0) { int count = Math.Min(buffer.Length, 32768); await outgoing.Writer.WriteAsync(buffer[..count].ToArray(), cancellationToken); buffer = buffer[count..]; }
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, life.Token).GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, life.Token).GetAwaiter().GetResult();
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected override void Dispose(bool disposing) { if (disposing) { life.Cancel(); incoming.Writer.TryComplete(); outgoing.Writer.TryComplete(); } base.Dispose(disposing); }
    public override async ValueTask DisposeAsync() { Dispose(); await pump; life.Dispose(); GC.SuppressFinalize(this); }
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
