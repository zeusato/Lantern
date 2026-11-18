using System.Text.Json;

namespace Lantern.Core;

public static class Codec
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Json) ?? throw new InvalidDataException("Dữ liệu trống.");
}

public sealed class Peer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string LocalAlias { get; set; } = "";
    public string Avatar { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(LocalAlias) ? Name : LocalAlias;
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public bool Trusted { get; set; }
    public bool Blocked { get; set; }
    public DateTimeOffset Seen { get; set; }
    public bool Connected { get; set; }
    public string Presence { get; set; } = "online";
    public int ProtocolVersion { get; set; }
    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(2).Select(x => char.ToUpperInvariant(x[0])));
}

public sealed class ChatGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public List<string> Members { get; set; } = [];
    public long Revision { get; set; }
    public Dictionary<string, GroupMemberAccess> MemberAccess { get; set; } = [];
    public long OwnerGeneration { get; set; }
    public string OwnershipId { get; set; } = "";
    public bool Deleted { get; set; }
    public bool CaptureProtected { get; set; }
    public bool ForwardAllowed { get; set; } = true;
}
public sealed record GroupMemberAccess(long JoinedRevision, bool IncludeHistory);
public sealed class ConversationPolicy
{
    public string Id { get; set; } = "";
    public int DisappearSeconds { get; set; }
    public long Revision { get; set; }
    public string Actor { get; set; } = "";
}
public sealed class ContactCard
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; }
}

public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
    public bool Outgoing { get; set; }
    public string Kind { get; set; } = "text";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public string Hash { get; set; } = "";
    public long Transferred { get; set; }
    public string Status { get; set; } = "queued";
    public Dictionary<string, string> Delivery { get; set; } = [];
    public string GroupId { get; set; } = "";
    public long GroupRevision { get; set; }
    public bool Forwarded { get; set; }
    public string Error { get; set; } = "";
    public string ReplyToId { get; set; } = "";
    public ContactCard? Contact { get; set; }
    public int DisappearAfterSeconds { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Dictionary<string, string> Reactions { get; set; } = [];
    public Dictionary<string, long> ReactionVersions { get; set; } = [];
}

public sealed class WireMessage
{
    public int Version { get; set; }
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public ChatMessage? Message { get; set; }
    public ChatGroup? Group { get; set; }
    public long Offset { get; set; }
    public string Data { get; set; } = "";
}

public sealed record PairRequest(string PeerId, string Name, string Address, string VerificationCode);
public sealed record TransferUpdate(string MessageId, long Bytes, long Total);
public sealed class AppSettings
{
    public string Name { get; set; } = "";
    public string Theme { get; set; } = "system";
    public string ColorTheme { get; set; } = "jade";
    public bool StartWithWindows { get; set; } = true;
    public int Port { get; set; } = 45853;
    public bool Onboarded { get; set; }
    public bool AutoEmoticons { get; set; } = true;
    public bool MessageToasts { get; set; } = true;
    public string MessageToastMode { get; set; } = "private";
    public bool CloseToTray { get; set; }
    public bool DoNotDisturb { get; set; }
    public bool AutoUpdates { get; set; } = true;
}
