namespace Lantern.Core;

public sealed partial class PeerNode
{
    private readonly SemaphoreSlim groupChanges = new(1, 1);

    public async Task<ChatGroup> AddGroupMembersAsync(string groupId, IEnumerable<string> memberIds, bool includeHistory)
    {
        await groupChanges.WaitAsync(lifetime.Token);
        try
        {
            var group = Store.Groups().Find(g => g.Id == groupId) ?? throw new ArgumentException("Không tìm thấy nhóm.");
            if (group.OwnerId != Identity.Id) throw new InvalidOperationException("Chỉ chủ nhóm hiện tại được thêm thành viên.");
            var added = memberIds.Distinct().Except(group.Members).ToArray();
            if (added.Length == 0) throw new ArgumentException("Chọn ít nhất một thành viên mới.");
            if (group.Members.Count + added.Length > 32) throw new ArgumentException("Nhóm có tối đa 32 thành viên.");
            foreach (var id in group.Members.Concat(added).Where(id => id != Identity.Id))
            {
                if (!peers.TryGetValue(id, out var peer) || !peer.Trusted || peer.Blocked) throw new InvalidOperationException("Cần ghép đôi với tất cả thành viên trước khi thêm người.");
                int requiredVersion = group.CaptureProtected || !group.ForwardAllowed ? 5 : group.OwnerGeneration > 0 ? 4 : 3;
                if (peer.ProtocolVersion < requiredVersion) throw new InvalidOperationException("Tất cả thành viên cần cập nhật Lantern để dùng chức năng thêm người vào nhóm.");
            }
            group.Revision = checked(group.Revision + 1);
            foreach (var id in added) { group.Members.Add(id); group.MemberAccess[id] = new(group.Revision, includeHistory); }
            Store.ApplyGroup(group);
            // Send the roster before any history. Offline members receive the same
            // version on reconnect; each author replays only their own messages.
            foreach (var id in group.Members.Where(id => id != Identity.Id)) await SendTo(id, new WireMessage { Type = "group", Group = group });
            PrepareGroupHistory(group);
            foreach (var id in added) Track(Replay(id));
            Changed?.Invoke(); return group;
        }
        finally { groupChanges.Release(); }
    }

    private void PrepareGroupHistory(ChatGroup group)
    {
        if (group.MemberAccess.Count == 0) return;
        foreach (var message in Store.Messages(group.Id, limit: int.MaxValue).Where(m => m.Outgoing && m.SenderId == Identity.Id && (!m.ExpiresAt.HasValue || m.ExpiresAt > DateTimeOffset.UtcNow)))
        {
            var recipients = group.MemberAccess.Keys.Where(id => id != Identity.Id && !message.Delivery.ContainsKey(id) && GroupMembership.CanRead(group, id, message)).ToArray();
            if (recipients.Length == 0) continue;
            Store.UpdateMessage(message.Id, current =>
            {
                foreach (var id in recipients) current.Delivery.TryAdd(id, current.Kind is "text" or "contact" ? "queued" : "offered");
            });
        }
    }

    private ChatMessage ForRecipient(ChatMessage message, string peerId)
    {
        var wire = ForWire(message);
        var group = message.GroupId.Length == 0 ? null : Store.Groups().Find(g => g.Id == message.GroupId);
        if (group is not null && GroupMembership.IsHistory(group, peerId, message)) wire.ExpiresAt = message.ExpiresAt;
        return wire;
    }

    private long GroupRevision(string conversationId) => Store.Groups().Find(g => g.Id == conversationId)?.Revision ?? 0;
}
