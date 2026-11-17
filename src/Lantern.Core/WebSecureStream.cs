using System.Buffers.Binary;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Lantern.Core;

// Browser transport v1: authenticated ephemeral P-256 ECDH, HKDF-SHA256 and
// directional AES-256-GCM records. Relay capabilities do not decrypt messages.
// Both ECDSA signatures bind both identities, route, session and ephemeral keys.
public sealed class WebSecureStream : Stream
{
    private readonly Stream inner;
    private readonly AesGcm send, receive;
    private readonly byte[] context;
    private ulong sent, received;
    private byte[] current = []; private int position;
    public string RemoteId { get; }
    private WebSecureStream(Stream inner, string remote, byte[] keys, byte[] context)
    { this.inner = inner; RemoteId = remote; this.context = context; receive = new(keys.AsSpan(0, 32), 16); send = new(keys.AsSpan(32, 32), 16); CryptographicOperations.ZeroMemory(keys); }
    private sealed class Hello
    {
        public string Key { get; set; } = "";
        public string Ephemeral { get; set; } = "";
        public string Random { get; set; } = "";
        public string Signature { get; set; } = "";
    }
    private static byte[] Decode(string text, int max)
    { if (text.Length > max * 2) throw new AuthenticationException(); var value = Convert.FromBase64String(text); if (value.Length > max) throw new AuthenticationException(); return value; }
    public static async Task<WebSecureStream> AcceptAsync(Stream inner, Identity identity, string route, string session, CancellationToken ct)
    {
        var raw = await ReadFrame(inner, 4096, ct);
        var hello = JsonSerializer.Deserialize<Hello>(raw, MobileProtocol.Json) ?? throw new AuthenticationException();
        var key = Decode(hello.Key, 128); var ephemeral = Decode(hello.Ephemeral, 128);
        if (Decode(hello.Random, 32).Length != 32) throw new AuthenticationException();
        using var signing = ECDsa.Create(); signing.ImportSubjectPublicKeyInfo(key, out var used);
        if (used != key.Length || signing.KeySize != 256 || signing.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value) throw new AuthenticationException();
        using var remote = ECDiffieHellman.Create(); remote.ImportSubjectPublicKeyInfo(ephemeral, out used);
        if (used != ephemeral.Length || remote.KeySize != 256 || remote.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value) throw new AuthenticationException();
        var clientText = string.Join("\n", "Lantern-web-client-v1", identity.Id, route, session, hello.Key, hello.Ephemeral, hello.Random);
        if (!signing.VerifyData(Encoding.UTF8.GetBytes(clientText), Decode(hello.Signature, 64), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new AuthenticationException();
        using var own = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var ownSign = identity.Certificate.GetECDsaPrivateKey() ?? throw new AuthenticationException();
        var response = new Hello { Key = Convert.ToBase64String(ownSign.ExportSubjectPublicKeyInfo()), Ephemeral = Convert.ToBase64String(own.ExportSubjectPublicKeyInfo()), Random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var transcript = Encoding.UTF8.GetBytes(string.Join("\n", "Lantern-web-server-v1", clientText, response.Key, response.Ephemeral, response.Random));
        response.Signature = Convert.ToBase64String(ownSign.SignData(transcript, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var context = SHA256.HashData(transcript); var shared = own.DeriveRawSecretAgreement(remote.PublicKey);
        byte[] keys;
        try { keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 64, context, "Lantern-web-keys-v1"u8.ToArray()); }
        finally { CryptographicOperations.ZeroMemory(shared); }
        await WriteFrame(inner, JsonSerializer.SerializeToUtf8Bytes(response, MobileProtocol.Json), ct);
        return new(inner, Convert.ToHexString(SHA256.HashData(key)), keys, context);
    }
    private byte[] Aad(byte direction, ulong sequence)
    { var value = new byte[41]; context.CopyTo(value, 0); value[32] = direction; BinaryPrimitives.WriteUInt64BigEndian(value.AsSpan(33), sequence); return value; }
    private static byte[] Nonce(ulong sequence)
    { var value = new byte[12]; BinaryPrimitives.WriteUInt64BigEndian(value.AsSpan(4), sequence); return value; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (buffer.Length == 0) return 0;
        if (position == current.Length)
        {
            var record = await ReadFrame(inner, 32768 + 16, ct);
            if (record.Length <= 16 || received == ulong.MaxValue) throw new IOException("Kênh web không hợp lệ.");
            current = new byte[record.Length - 16]; position = 0;
            receive.Decrypt(Nonce(++received), record.AsSpan(0, current.Length), record.AsSpan(current.Length), current, Aad(0, received));
        }
        var count = Math.Min(buffer.Length, current.Length - position); current.AsMemory(position, count).CopyTo(buffer); position += count; return count;
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        while (buffer.Length > 0)
        {
            if (sent == ulong.MaxValue) throw new IOException("Kênh web cần nối lại.");
            int length = Math.Min(buffer.Length, 32768); var record = new byte[length + 16];
            send.Encrypt(Nonce(++sent), buffer.Span[..length], record.AsSpan(0, length), record.AsSpan(length), Aad(1, sent));
            await WriteFrame(inner, record, ct); buffer = buffer[length..];
        }
    }
    private static async Task<byte[]> ReadFrame(Stream stream, int max, CancellationToken ct)
    { var header = new byte[4]; await stream.ReadExactlyAsync(header, ct); int count = BinaryPrimitives.ReadInt32BigEndian(header); if (count < 1 || count > max) throw new IOException("Sai kích thước khung web."); var data = new byte[count]; await stream.ReadExactlyAsync(data, ct); return data; }
    private static async Task WriteFrame(Stream stream, byte[] value, CancellationToken ct)
    { var frame = new byte[value.Length + 4]; BinaryPrimitives.WriteInt32BigEndian(frame, value.Length); value.CopyTo(frame, 4); await stream.WriteAsync(frame, ct); }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { send.Dispose(); receive.Dispose(); CryptographicOperations.ZeroMemory(current); } base.Dispose(disposing); }
}
