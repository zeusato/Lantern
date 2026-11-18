using System.Buffers.Binary;
using System.Net.Security;
using System.Text.Json;

namespace Lantern.Core;

public static class Protocol
{
    public const int MaxFrame = 256 * 1024;
    public const int ChunkSize = 64 * 1024;
    public const long MaxFileSize = 10L * 1024 * 1024 * 1024;
    public static async Task WriteAsync(Stream stream, WireMessage message, CancellationToken ct)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(message, Codec.Json);
        if (data.Length > MaxFrame) throw new InvalidDataException("Gói tin quá lớn.");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(data, ct); await stream.FlushAsync(ct);
    }
    public static async Task<WireMessage> ReadAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 2 || length > MaxFrame) throw new InvalidDataException("Độ dài gói tin không hợp lệ.");
        byte[] data = new byte[length]; await stream.ReadExactlyAsync(data, ct);
        var message = JsonSerializer.Deserialize<WireMessage>(data, Codec.Json) ?? throw new InvalidDataException();
        Validate(message); return message;
    }
    public static void Validate(WireMessage message)
    {
        if (message.Message is { } reply && (reply.ReplyToId is null || reply.Reactions is null || reply.ReactionVersions is null)) throw new InvalidDataException("Tin nhắn chứa trường null.");
        if (message.Type is null || message.Id is null || message.Name is null || message.Data is null) throw new InvalidDataException("Gói tin chứa trường null.");
        if (message.Message is { } m && (m.Id is null || m.ConversationId is null || m.SenderId is null || m.SenderName is null || m.Text is null || m.Kind is null || m.FileName is null || m.FilePath is null || m.Hash is null || m.Status is null || m.Error is null || m.GroupId is null || m.Delivery is null)) throw new InvalidDataException("Tin nhắn chứa trường null.");
        if (message.Group is { } g && (g.Id is null || g.Name is null || g.OwnerId is null || g.Members is null || g.Members.Any(id => id is null) || g.MemberAccess is null || g.MemberAccess.Any(p => p.Value is null))) throw new InvalidDataException("Nhóm chứa trường null.");
    }
    public static string SafeFileName(string file)
    {
        var name = Path.GetFileName(file.Replace('\\', '/'));
        name = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "attachment";
        return name.Length > 120 ? name[^120..] : name;
    }
    public static bool ValidId(string id) => id.Length == 32 && Guid.TryParseExact(id, "N", out _);
}

internal sealed class Session(string peerId, SslStream stream, IDisposable transport, bool outbound) : IDisposable
{
    public string PeerId { get; } = peerId;
    public SslStream Stream { get; } = stream;
    public bool Outbound { get; } = outbound;
    private readonly SemaphoreSlim writer = new(1, 1);
    public async Task SendAsync(WireMessage msg, CancellationToken ct)
    {
        await writer.WaitAsync(ct);
        try { await Protocol.WriteAsync(Stream, msg, ct); } finally { writer.Release(); }
    }
    public void Dispose() { Stream.Dispose(); transport.Dispose(); }
}
