using Microsoft.Data.Sqlite;

namespace Lantern.Core;

public sealed partial class LocalStore
{
    private void InitializeGroupActions()
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS group_actions(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS capture_policies(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS conversation_preferences(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS pinned_messages(id TEXT PRIMARY KEY,conversation TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    internal ChatGroup? GroupRecord(string id, SqliteTransaction? transaction = null)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.Transaction = transaction;
            cmd.CommandText = "SELECT data FROM groups WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteScalar() is string data ? Codec.Decode<ChatGroup>(Open(data)) : null;
        }
    }
    internal List<(SignedGroupAction Envelope, GroupActionBody Body)> GroupActionRecords() =>
        ReadAll<SignedGroupAction>("group_actions").Select(a => (Envelope: a, Body: Codec.Decode<GroupActionBody>(a.Payload)))
            .OrderBy(a => a.Body.Group.OwnerGeneration).ThenBy(a => a.Body.Group.Revision).ThenBy(a => a.Body.Kind == "leave" ? 0 : a.Body.Kind == "transfer" ? 1 : 2).ToList();

    internal ChatGroup FilterDepartures(ChatGroup group)
    {
        var copy = Codec.Decode<ChatGroup>(Codec.Encode(group));
        foreach (var (_, action) in GroupActionRecords().Where(a => a.Body.Kind == "leave" && a.Body.Group.Id == group.Id))
        {
            if (action.ActorId == copy.OwnerId) continue; // A racing successor completes their own signed handoff.
            if (copy.MemberAccess.GetValueOrDefault(action.ActorId)?.JoinedRevision != action.Group.MemberAccess.GetValueOrDefault(action.ActorId)?.JoinedRevision) continue;
            copy.Members.Remove(action.ActorId); copy.MemberAccess.Remove(action.ActorId);
        }
        return copy;
    }

    internal (bool Applied, List<ChatMessage> Deleted) ApplyGroupAction(SignedGroupAction envelope, string localId)
    {
        var action = GroupActions.Verify(envelope);
        lock (gate)
        {
            var records = GroupActionRecords();
            if (records.Any(a => a.Body.Id == action.Id)) return (false, []);
            var current = GroupRecord(action.Group.Id);
            if (current is { Deleted: true }) return (false, []);
            if (current is null)
            {
                if (action.Kind == "leave" || action.Group.OwnerGeneration != 0 || !action.Group.Members.Contains(localId)) return (false, []);
                current = action.Group;
            }
            if (action.Kind == "transfer" && action.Group.OwnerGeneration < current.OwnerGeneration)
            {
                // Retain a verified predecessor when the newest roster arrived first.
                bool predecessor = current.OwnershipId == action.Id && current.OwnerId == action.SuccessorId || records.Any(a => a.Body.Group.OwnershipId == action.Id && a.Body.Group.OwnerId == action.SuccessorId);
                if (predecessor) Save("group_actions", action.Id, Codec.Encode(envelope));
                return (predecessor, []);
            }
            ChatGroup next;
            if (action.Kind == "leave")
            {
                if (!current.Members.Contains(action.ActorId) || current.MemberAccess.GetValueOrDefault(action.ActorId)?.JoinedRevision != action.Group.MemberAccess.GetValueOrDefault(action.ActorId)?.JoinedRevision) return (false, []);
                if (current.OwnerId == action.ActorId)
                {
                    // A member may leave concurrently with being selected as owner.
                    // Keep the signed intent so that owner can finish a handoff.
                    if (action.Group.OwnerId == action.ActorId) throw new InvalidDataException("Chủ nhóm cần chuyển quyền trước khi rời.");
                    Save("group_actions", action.Id, Codec.Encode(envelope)); return (true, []);
                }
                next = current;
                next.Members.Remove(action.ActorId); next.MemberAccess.Remove(action.ActorId);
            }
            else
            {
                if (action.Group.OwnerGeneration != current.OwnerGeneration || action.Group.OwnershipId != current.OwnershipId) return (false, []);
                if (action.ActorId != current.OwnerId) throw new InvalidDataException("Chỉ chủ nhóm hiện tại được thực hiện thao tác.");
                if (action.Group.Revision < current.Revision) return (false, []);
                next = FilterDepartures(action.Group);
                _ = GroupMembership.ShouldReplace(current, next);
                if (action.Kind == "delete") next.Deleted = true;
                else
                {
                    next.OwnerId = action.SuccessorId; next.OwnerGeneration++; next.OwnershipId = action.Id; next.Revision++;
                    next.Members.Remove(action.ActorId); next.MemberAccess.Remove(action.ActorId);
                    if (!next.Members.Contains(next.OwnerId))
                    {
                        next.Members.Add(next.OwnerId);
                        if (action.Group.MemberAccess.TryGetValue(next.OwnerId, out var access)) next.MemberAccess[next.OwnerId] = access;
                    }
                }
            }
            var deleted = next.Deleted ? Messages(next.Id, limit: int.MaxValue) : [];
            using var tx = db.BeginTransaction();
            void SaveInTransaction(string table, string id, string data)
            {
                using var cmd = db.CreateCommand(); cmd.Transaction = tx;
                cmd.CommandText = $"INSERT INTO {table}(id,data) VALUES($id,$data) ON CONFLICT(id) DO UPDATE SET data=$data";
                cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$data", Seal(data)); cmd.ExecuteNonQuery();
            }
            SaveInTransaction("group_actions", action.Id, Codec.Encode(envelope)); SaveInTransaction("groups", next.Id, Codec.Encode(next));
            if (next.Deleted)
            {
                using var cmd = db.CreateCommand(); cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO deleted_messages(id) SELECT id FROM messages WHERE conversation=$c; DELETE FROM unread_messages WHERE id IN (SELECT id FROM messages WHERE conversation=$c); DELETE FROM message_expiry WHERE id IN (SELECT id FROM messages WHERE conversation=$c); DELETE FROM pinned_messages WHERE conversation=$c; DELETE FROM messages WHERE conversation=$c; DELETE FROM policies WHERE id=$c; DELETE FROM capture_policies WHERE id=$c; DELETE FROM conversation_preferences WHERE id=$c; INSERT INTO history_revisions(conversation,revision) VALUES($c,1) ON CONFLICT(conversation) DO UPDATE SET revision=revision+1";
                cmd.Parameters.AddWithValue("$c", next.Id); cmd.ExecuteNonQuery();
            }
            tx.Commit(); MessagesChanged(next.Id);
            return (true, deleted);
        }
    }
}
