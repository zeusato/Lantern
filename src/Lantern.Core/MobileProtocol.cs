using System.Buffers.Binary;
using System.Text.Json;

namespace Lantern.Core;

public sealed class MobilePacket
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Conversation { get; set; } = "";
    public string Text { get; set; } = "";
    public string Password { get; set; } = "";
    public string Nonce { get; set; } = "";
    public string Name { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string ReplyTo { get; set; } = "";
    public string Emoji { get; set; } = "";
    public string Data { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Hash { get; set; } = "";
    public long Offset { get; set; }
    public long Size { get; set; }
    public bool Locked { get; set; }
    public List<MobileConversation> Conversations { get; set; } = [];
    public List<ChatMessage> Messages { get; set; } = [];
    public List<string> VisibleIds { get; set; } = [];
    public List<string> Capabilities { get; set; } = [];
}
public sealed record MobileConversation(string Id, string Name, string Presence, int Unread, bool Locked, bool IsGroup, bool Protected = false);
public static class MobileProtocol
{
    public const int Version = 1;
    public const int MaxFrame = 2 * 1024 * 1024;
    public const long MaxMobileFile = 100 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, MaxDepth = 20 };
    public static List<ChatMessage> BoundedHistory(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>(); int bytes = 4096;
        foreach (var message in messages.Reverse())
        {
            message.FilePath = ""; message.Error = "";
            bytes += JsonSerializer.SerializeToUtf8Bytes(message, Json).Length + 1;
            if (bytes > MaxFrame) break;
            result.Add(message);
        }
        result.Reverse(); return result;
    }
    public static async Task WriteAsync(Stream stream, MobilePacket packet, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, Json);
        if (bytes.Length > MaxFrame) throw new InvalidDataException("Nội dung quá lớn.");
        var frame = new byte[4 + bytes.Length]; BinaryPrimitives.WriteInt32BigEndian(frame, bytes.Length); bytes.CopyTo(frame, 4);
        await stream.WriteAsync(frame, ct); await stream.FlushAsync(ct);
    }
    public static async Task<MobilePacket> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > MaxFrame) throw new InvalidDataException("Gói mobile không hợp lệ.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct);
        var packet = JsonSerializer.Deserialize<MobilePacket>(bytes, Json) ?? throw new InvalidDataException();
        if (packet.VisibleIds is null || packet.VisibleIds.Count > 100 || packet.VisibleIds.Any(id => id is null || !Protocol.ValidId(id))) throw new InvalidDataException("Danh sách tin đã đọc không hợp lệ.");
        if (packet.Type is null || packet.Id is null || packet.Conversation is null || packet.Text is null || packet.Password is null || packet.Nonce is null || packet.Name is null || packet.MessageId is null || packet.Emoji is null || packet.ReplyTo is null || packet.Data is null || packet.FileName is null || packet.Hash is null) throw new InvalidDataException("Trường mobile không hợp lệ.");
        if (packet.Text.Length > 16000 || packet.Password.Length > 256 || packet.Name.Length > 80 || packet.Nonce.Length > 256 || packet.Id.Length > 64 || packet.Conversation.Length > 128 || packet.Data.Length > 90000) throw new InvalidDataException("Gói mobile vượt giới hạn.");
        return packet;
    }
}
