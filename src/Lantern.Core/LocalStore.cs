using Microsoft.Data.Sqlite;

namespace Lantern.Core;

public sealed class LocalStore : IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();
    private readonly WorkspaceVault? vault;
    public string DirectoryPath { get; }
    public string MediaPath => Path.Combine(DirectoryPath, "media");
    public LocalStore(string directory, WorkspaceVault? vault = null)
    {
        this.vault = vault;
        directory = Path.GetFullPath(directory);
        DirectoryPath = directory;
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(MediaPath);
        db = new SqliteConnection($"Data Source={Path.Combine(directory, "history.db")};Pooling=False");
        db.Open();
        using (var revisions = db.CreateCommand()) { revisions.CommandText = "CREATE TABLE IF NOT EXISTS history_revisions(conversation TEXT PRIMARY KEY, revision INTEGER NOT NULL)"; revisions.ExecuteNonQuery(); }
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA secure_delete=ON; CREATE TABLE IF NOT EXISTS peers(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS messages(id TEXT PRIMARY KEY,conversation TEXT NOT NULL,time TEXT NOT NULL,data TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_messages_conversation ON messages(conversation,time); CREATE TABLE IF NOT EXISTS groups(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS deleted_messages(id TEXT PRIMARY KEY);";
        cmd.ExecuteNonQuery();
        using var extra = db.CreateCommand(); extra.CommandText = "CREATE TABLE IF NOT EXISTS policies(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS expired_messages(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS message_expiry(id TEXT PRIMARY KEY,expires TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_message_expiry ON message_expiry(expires);"; extra.ExecuteNonQuery();
        if (vault is not null) MigrateEncryption();
        InitializeUnread();
    }
    private void InitializeUnread()
    {
        using var schema = db.CreateCommand();
        schema.CommandText = "CREATE TABLE IF NOT EXISTS unread_messages(id TEXT PRIMARY KEY); CREATE TABLE IF NOT EXISTS local_migrations(name TEXT PRIMARY KEY);";
        schema.ExecuteNonQuery();
        using var check = db.CreateCommand(); check.CommandText = "SELECT 1 FROM local_migrations WHERE name='unread-v1'";
        if (check.ExecuteScalar() is not null) return;
        // Preserve unread text from older versions. Previously accepted/declined files were already seen.
        var pending = Messages(limit: int.MaxValue).Where(m => !m.Outgoing && (m.Kind == "text" ? m.Status != "read" : m.Status is "offered" or "receiving" or "error")).ToList();
        using var tx = db.BeginTransaction();
        foreach (var message in pending) AddUnread(message.Id, tx);
        using var done = db.CreateCommand(); done.Transaction = tx; done.CommandText = "INSERT INTO local_migrations(name) VALUES('unread-v1')"; done.ExecuteNonQuery(); tx.Commit();
    }
    private void AddUnread(string id, SqliteTransaction transaction)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = transaction;
        cmd.CommandText = "INSERT OR IGNORE INTO unread_messages(id) VALUES($id)"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
    }
    public int UnreadCount()
    {
        lock (gate) { using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM unread_messages"; return Convert.ToInt32(cmd.ExecuteScalar()); }
    }
    public Dictionary<string, int> UnreadCounts()
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT m.conversation,COUNT(*) FROM unread_messages u JOIN messages m ON m.id=u.id GROUP BY m.conversation";
            using var reader = cmd.ExecuteReader(); var counts = new Dictionary<string, int>();
            while (reader.Read()) counts[reader.GetString(0)] = reader.GetInt32(1); return counts;
        }
    }
    public bool SaveIncomingMessage(ChatMessage message)
    {
        if (message.Outgoing) throw new ArgumentException("Chỉ đánh dấu tin nhận là chưa đọc.");
        lock (gate)
        {
            if (WasDeleted(message.Id) || Message(message.Id) is not null) return false;
            using var tx = db.BeginTransaction(); WriteMessage(message, tx); AddUnread(message.Id, tx); tx.Commit(); return true;
        }
    }
    public List<ChatMessage> MarkConversationRead(string conversationId, IReadOnlySet<string>? visibleMessages = null)
    {
        lock (gate)
        {
            var unread = new List<ChatMessage>();
            using (var read = db.CreateCommand())
            {
                read.CommandText = "SELECT m.data FROM messages m JOIN unread_messages u ON u.id=m.id WHERE m.conversation=$c"; read.Parameters.AddWithValue("$c", conversationId);
                using var reader = read.ExecuteReader(); while (reader.Read()) unread.Add(Codec.Decode<ChatMessage>(Open(reader.GetString(0))));
            }
            unread.RemoveAll(message => message.Kind == "image" && message.Status is "offered" or "receiving");
            if (visibleMessages is not null) unread.RemoveAll(message => !visibleMessages.Contains(message.Id));
            if (unread.Count == 0) return unread;
            using var tx = db.BeginTransaction();
            foreach (var message in unread)
            {
                if (message.Kind is "text" or "contact") message.Status = "read";
                if (message.DisappearAfterSeconds > 0) message.ExpiresAt ??= DateTimeOffset.UtcNow.AddSeconds(message.DisappearAfterSeconds);
                WriteMessage(message, tx);
            }
            using var clear = db.CreateCommand(); clear.Transaction = tx;
            clear.CommandText = "DELETE FROM unread_messages WHERE id=$id"; var id = clear.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
            foreach (var message in unread) { id.Value = message.Id; clear.ExecuteNonQuery(); } tx.Commit();
            return unread;
        }
    }
    private string Seal(string value) => vault?.Seal(value) ?? value;
    private string Open(string value) => vault?.Unseal(value) ?? (value.StartsWith("enc:v1:", StringComparison.Ordinal) ? throw new UnauthorizedAccessException("Cần mở khóa workspace để đọc lịch sử.") : value);
    private void MigrateEncryption()
    {
        var changed = false;
        foreach (var table in new[] { "peers", "messages", "groups", "policies", "expired_messages" })
        {
            var rows = new List<(string Id, string Data)>();
            using (var read = db.CreateCommand()) { read.CommandText = $"SELECT id,data FROM {table} WHERE data NOT LIKE 'enc:v1:%'"; using var reader = read.ExecuteReader(); while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1))); }
            if (rows.Count == 0) continue;
            using var tx = db.BeginTransaction();
            foreach (var row in rows) { using var write = db.CreateCommand(); write.Transaction = tx; write.CommandText = $"UPDATE {table} SET data=$d WHERE id=$i"; write.Parameters.AddWithValue("$d", Seal(row.Data)); write.Parameters.AddWithValue("$i", row.Id); write.ExecuteNonQuery(); }
            tx.Commit(); changed = true;
        }
        if (changed) { using var clean = db.CreateCommand(); clean.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM; PRAGMA wal_checkpoint(TRUNCATE);"; clean.ExecuteNonQuery(); }
    }
    public void SavePeer(Peer value) => Save("peers", value.Id, Codec.Encode(value));
    public void SaveGroup(ChatGroup value) => Save("groups", value.Id, Codec.Encode(value));
    private void Save(string table, string id, string value)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"INSERT INTO {table}(id,data) VALUES($id,$data) ON CONFLICT(id) DO UPDATE SET data=$data";
            cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$data", Seal(value)); cmd.ExecuteNonQuery();
        }
    }
    public List<Peer> Peers() => ReadAll<Peer>("peers");
    public List<ChatGroup> Groups() => ReadAll<ChatGroup>("groups");
    public List<ConversationPolicy> Policies() => ReadAll<ConversationPolicy>("policies");
    public List<ChatMessage> ExpiredMessages() => ReadAll<ChatMessage>("expired_messages");
    public bool SavePolicy(ConversationPolicy policy)
    {
        lock (gate)
        {
            var current = Policies().Find(p => p.Id == policy.Id);
            if (current is not null && (current.Revision > policy.Revision || current.Revision == policy.Revision && string.CompareOrdinal(current.Actor, policy.Actor) >= 0)) return false;
            Save("policies", policy.Id, Codec.Encode(policy)); return true;
        }
    }
    public bool SetMessageExpiry(string id, DateTimeOffset deadline)
    {
        lock (gate)
        {
            var message = Message(id);
            if (message is null || message.DisappearAfterSeconds == 0 || message.ExpiresAt is { } current && current <= deadline) return false;
            message.ExpiresAt = deadline; SaveMessage(message); return true;
        }
    }
    public List<ChatMessage> RemoveExpired(DateTimeOffset now)
    {
        lock (gate)
        {
            var messages = new List<ChatMessage>();
            using (var find = db.CreateCommand())
            {
                find.CommandText = "SELECT m.data FROM message_expiry e JOIN messages m ON m.id=e.id WHERE e.expires <= $now"; find.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
                using var reader = find.ExecuteReader(); while (reader.Read()) messages.Add(Codec.Decode<ChatMessage>(Open(reader.GetString(0))));
            }
            using var tx = db.BeginTransaction();
            foreach (var message in messages)
            {
                var tombstone = new ChatMessage { Id = message.Id, ConversationId = message.ConversationId, SenderId = message.SenderId, GroupId = message.GroupId, DisappearAfterSeconds = message.DisappearAfterSeconds, ExpiresAt = message.ExpiresAt };
                using var cmd = db.CreateCommand(); cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR REPLACE INTO expired_messages(id,data) VALUES($id,$data); INSERT OR IGNORE INTO deleted_messages(id) VALUES($id); DELETE FROM unread_messages WHERE id=$id; DELETE FROM message_expiry WHERE id=$id; DELETE FROM messages WHERE id=$id;";
                cmd.Parameters.AddWithValue("$id", message.Id); cmd.Parameters.AddWithValue("$data", Seal(Codec.Encode(tombstone))); cmd.ExecuteNonQuery();
            }
            tx.Commit(); return messages;
        }
    }
    private List<T> ReadAll<T>(string table)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = $"SELECT data FROM {table}";
            using var reader = cmd.ExecuteReader(); var result = new List<T>();
            while (reader.Read()) result.Add(Codec.Decode<T>(Open(reader.GetString(0))));
            return result;
        }
    }
    public void SaveMessage(ChatMessage value)
    {
        lock (gate) { using var tx = db.BeginTransaction(); WriteMessage(value, tx); tx.Commit(); }
    }
    private void WriteMessage(ChatMessage value, SqliteTransaction? transaction = null)
    {
            using var cmd = db.CreateCommand(); cmd.Transaction = transaction; cmd.CommandText = "INSERT INTO messages(id,conversation,time,data) SELECT $id,$c,$t,$d WHERE NOT EXISTS(SELECT 1 FROM deleted_messages WHERE id=$id) ON CONFLICT(id) DO UPDATE SET data=$d";
            cmd.Parameters.AddWithValue("$id", value.Id); cmd.Parameters.AddWithValue("$c", value.ConversationId);
            cmd.Parameters.AddWithValue("$t", value.Time.ToString("O")); cmd.Parameters.AddWithValue("$d", Seal(Codec.Encode(value))); cmd.ExecuteNonQuery();
            using var expiry = db.CreateCommand(); expiry.Transaction = transaction;
            expiry.CommandText = "DELETE FROM message_expiry WHERE id=$id; INSERT INTO message_expiry(id,expires) SELECT $id,$expires WHERE $expires IS NOT NULL AND EXISTS(SELECT 1 FROM messages WHERE id=$id);";
            expiry.Parameters.AddWithValue("$id", value.Id); expiry.Parameters.AddWithValue("$expires", (object?)value.ExpiresAt?.UtcDateTime.ToString("O") ?? DBNull.Value); expiry.ExecuteNonQuery();
    }
    public ChatMessage? Message(string id)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT data FROM messages WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteScalar() is string raw ? Codec.Decode<ChatMessage>(Open(raw)) : null;
        }
    }
    public void UpdateMessage(string id, Action<ChatMessage> change)
    {
        lock (gate) { var value = Message(id); if (value is null) return; change(value); SaveMessage(value); }
    }
    public List<ChatMessage> Messages(string? conversation = null, string? search = null, int limit = 300)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT data FROM messages WHERE ($c IS NULL OR conversation=$c) ORDER BY time DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$c", (object?)conversation ?? DBNull.Value); cmd.Parameters.AddWithValue("$n", limit);
            using var reader = cmd.ExecuteReader(); var result = new List<ChatMessage>();
            while (reader.Read()) { var m = Codec.Decode<ChatMessage>(Open(reader.GetString(0))); if (string.IsNullOrWhiteSpace(search) || (m.Text + " " + m.FileName).Contains(search, StringComparison.CurrentCultureIgnoreCase)) result.Add(m); }
            result.Reverse(); return result;
        }
    }
    public List<ChatMessage> Pending(string peerId) => Messages(limit: int.MaxValue).Where(m => m.Outgoing && m.Delivery.TryGetValue(peerId, out var status) && status is "queued" or "offered").ToList();
    public bool WasDeleted(string id)
    {
        lock (gate) { using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT 1 FROM deleted_messages WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); return cmd.ExecuteScalar() is not null; }
    }
    public List<ChatMessage> DeleteConversation(string id) => DeleteMessages(id, null);
    public Dictionary<string, long> HistoryVersions()
    {
        lock (gate) { using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT conversation,revision FROM history_revisions"; using var reader = cmd.ExecuteReader(); var values = new Dictionary<string, long>(); while (reader.Read()) values[reader.GetString(0)] = reader.GetInt64(1); return values; }
    }
    public List<ChatMessage> DeleteMessages(string conversation, IReadOnlySet<string>? ids)
    {
        lock (gate)
        {
            var messages = Messages(conversation, limit: int.MaxValue).Where(m => ids is null || ids.Contains(m.Id)).ToList();
            using var tx = db.BeginTransaction();
            using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO deleted_messages(id) VALUES($id); DELETE FROM unread_messages WHERE id=$id; DELETE FROM message_expiry WHERE id=$id; DELETE FROM expired_messages WHERE id=$id; DELETE FROM messages WHERE id=$id;";
            var parameter = cmd.Parameters.Add("$id", SqliteType.Text);
            foreach (var message in messages) { parameter.Value = message.Id; cmd.ExecuteNonQuery(); }
            if (messages.Count > 0)
            {
                using var revision = db.CreateCommand(); revision.Transaction = tx;
                revision.CommandText = "INSERT INTO history_revisions(conversation,revision) VALUES($c,1) ON CONFLICT(conversation) DO UPDATE SET revision=revision+1"; revision.Parameters.AddWithValue("$c", conversation); revision.ExecuteNonQuery();
            }
            tx.Commit(); return messages;
        }
    }
    public void Dispose() { lock (gate) db.Dispose(); }
}
