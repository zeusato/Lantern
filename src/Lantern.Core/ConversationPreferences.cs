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
}

public sealed partial class LocalStore
{
    public List<ConversationPreferences> Preferences() => ReadAll<ConversationPreferences>("conversation_preferences");
    public ConversationPreferences Preference(string id) => Preferences().Find(p => p.Id == id) ?? new() { Id = id };
    public void SavePreference(ConversationPreferences value) => Save("conversation_preferences", value.Id, Codec.Encode(value));
    public List<ConversationRestrictions> Restrictions() => ReadAll<ConversationRestrictions>("capture_policies");
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
    public HashSet<string> PinnedMessages(string conversation)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT p.id FROM pinned_messages p JOIN messages m ON p.id=m.id WHERE p.conversation=$c"; cmd.Parameters.AddWithValue("$c", conversation);
            using var reader = cmd.ExecuteReader(); var ids = new HashSet<string>(); while (reader.Read()) ids.Add(reader.GetString(0)); return ids;
        }
    }
    public void PinMessage(string id, bool pinned)
    {
        lock (gate)
        {
            var message = Message(id) ?? throw new ArgumentException("Tin nhắn không còn tồn tại.");
            using var cmd = db.CreateCommand(); cmd.CommandText = pinned ? "INSERT OR IGNORE INTO pinned_messages(id,conversation) VALUES($id,$c)" : "DELETE FROM pinned_messages WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id); if (pinned) cmd.Parameters.AddWithValue("$c", message.ConversationId); cmd.ExecuteNonQuery(); MessagesChanged(message.ConversationId);
        }
    }
}
