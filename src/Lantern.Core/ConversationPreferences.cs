namespace Lantern.Core;

public sealed class ConversationPreferences
{
    public string Id { get; set; } = "";
    public bool Pinned { get; set; }
    public string Category { get; set; } = "";
    public bool Hidden { get; set; }
}
public sealed class ConversationRestrictions
{
    public string Id { get; set; } = "";
    public string Actor { get; set; } = "";
    public long Revision { get; set; }
    public bool CaptureProtected { get; set; }
    public bool ForwardAllowed { get; set; } = true;
    public int OwnershipVersion { get; set; }
    public Dictionary<string, RestrictionClaim> Claims { get; set; } = [];
    public void NormalizeOwnership()
    {
        if (OwnershipVersion < 2 && Revision > 0 && Actor.Length == 64)
            Claims[Actor] = new() { Revision = Revision, CaptureProtected = CaptureProtected, ForwardAllowed = ForwardAllowed };
        OwnershipVersion = 2;
        CaptureProtected = Claims.Values.Any(c => c.CaptureProtected);
        ForwardAllowed = Claims.Values.All(c => c.ForwardAllowed);
    }
    public string[] CaptureOwners => Claims.Where(p => p.Value.CaptureProtected).Select(p => p.Key).ToArray();
    public string[] ForwardOwners => Claims.Where(p => !p.Value.ForwardAllowed).Select(p => p.Key).ToArray();
}
public sealed class RestrictionClaim
{
    public long Revision { get; set; }
    public bool CaptureProtected { get; set; }
    public bool ForwardAllowed { get; set; } = true;
}

public sealed partial class LocalStore
{
    public const int MaxPinnedMessages = 3;
    public List<ConversationPreferences> Preferences() => ReadAll<ConversationPreferences>("conversation_preferences");
    public ConversationPreferences Preference(string id) => Preferences().Find(p => p.Id == id) ?? new() { Id = id };
    public void SavePreference(ConversationPreferences value) => Save("conversation_preferences", value.Id, Codec.Encode(value));
    public List<ConversationRestrictions> Restrictions() => ReadAll<ConversationRestrictions>("capture_policies");
    public bool MergeRestrictionClaim(string conversation, string actor, RestrictionClaim incoming)
    {
        lock (gate)
        {
            var policy = Restrictions().Find(p => p.Id == conversation) ?? new() { Id = conversation };
            policy.NormalizeOwnership();
            if (policy.Claims.TryGetValue(actor, out var current) && current.Revision >= incoming.Revision) return false;
            policy.Claims[actor] = incoming; policy.Actor = actor; policy.Revision = Math.Max(policy.Revision, incoming.Revision);
            policy.NormalizeOwnership(); Save("capture_policies", conversation, Codec.Encode(policy)); return true;
        }
    }
    public bool SaveRestrictions(ConversationRestrictions policy)
    {
        lock (gate)
        {
            var current = Restrictions().Find(p => p.Id == policy.Id);
            if (current is not null && (current.Revision > policy.Revision || current.Revision == policy.Revision && string.CompareOrdinal(current.Actor, policy.Actor) >= 0)) return false;
            Save("capture_policies", policy.Id, Codec.Encode(policy)); return true;
        }
    }
    public Dictionary<string, DateTimeOffset> ConversationActivity()
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT conversation,MAX(julianday(time)) FROM messages GROUP BY conversation";
            using var reader = cmd.ExecuteReader(); var result = new Dictionary<string, DateTimeOffset>();
            while (reader.Read()) if (!reader.IsDBNull(1)) result[reader.GetString(0)] = DateTimeOffset.UnixEpoch.AddDays(reader.GetDouble(1) - 2440587.5);
            return result;
        }
    }
    public List<string> PinnedMessages(string conversation)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT p.id FROM pinned_messages p JOIN messages m ON p.id=m.id WHERE p.conversation=$c ORDER BY p.rowid DESC"; cmd.Parameters.AddWithValue("$c", conversation);
            using var reader = cmd.ExecuteReader(); var ids = new List<string>(); while (reader.Read()) ids.Add(reader.GetString(0)); return ids;
        }
    }
    public void PinMessage(string id, bool pinned)
    {
        lock (gate)
        {
            var message = Message(id) ?? throw new ArgumentException("Tin nhắn không còn tồn tại.");
            var pins = PinnedMessages(message.ConversationId);
            if (pinned && !pins.Contains(id) && pins.Count >= MaxPinnedMessages)
                throw new InvalidOperationException("Mỗi hội thoại chỉ ghim tối đa 3 tin nhắn. Hãy bỏ ghim một tin trước.");
            using var cmd = db.CreateCommand(); cmd.CommandText = pinned ? "INSERT OR IGNORE INTO pinned_messages(id,conversation) VALUES($id,$c)" : "DELETE FROM pinned_messages WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id); if (pinned) cmd.Parameters.AddWithValue("$c", message.ConversationId); cmd.ExecuteNonQuery(); MessagesChanged(message.ConversationId);
        }
    }
}
