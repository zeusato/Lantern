using System.Security.Cryptography;

namespace Lantern.Core;

public sealed partial class PeerNode
{
    private void RequireGroupCapability(ChatGroup group, int version)
    {
        if (group.Members.Where(id => id != Identity.Id).Any(id => !peers.TryGetValue(id, out var p) || p.ProtocolVersion < version))
            throw new InvalidOperationException("Tất cả thành viên cần cập nhật Lantern để dùng chức năng này.");
    }

    public async Task DeleteGroupAsync(string groupId)
    {
        await groupChanges.WaitAsync(lifetime.Token);
        try
        {
            var group = Store.Groups().Find(g => g.Id == groupId) ?? throw new ArgumentException("Nhóm không còn tồn tại.");
            if (group.OwnerId != Identity.Id) throw new InvalidOperationException("Chỉ chủ nhóm được xóa nhóm trên mọi máy.");
            RequireGroupCapability(group, 4);
            await CommitGroupAction(new GroupActionBody { Kind = "delete", Group = group });
        }
        finally { groupChanges.Release(); }
    }

    public async Task<string?> LeaveGroupAsync(string groupId, string? successorId = null)
    {
        await groupChanges.WaitAsync(lifetime.Token);
        try
        {
            var group = Store.Groups().Find(g => g.Id == groupId) ?? throw new ArgumentException("Nhóm không còn tồn tại.");
            if (!group.Members.Contains(Identity.Id)) throw new InvalidOperationException("Bạn đã rời nhóm.");
            RequireGroupCapability(group, 4);
            var kind = "leave";
            if (group.OwnerId == Identity.Id)
            {
                var remaining = group.Members.Where(id => id != Identity.Id).ToArray();
                if (remaining.Length == 0) kind = "delete";
                else
                {
                    if (!string.IsNullOrEmpty(successorId) && !remaining.Contains(successorId)) throw new ArgumentException("Người kế nhiệm phải là thành viên còn lại.");
                    successorId = string.IsNullOrEmpty(successorId) ? remaining[RandomNumberGenerator.GetInt32(remaining.Length)] : successorId;
                    kind = "transfer";
                }
            }
            else if (!string.IsNullOrEmpty(successorId)) throw new InvalidOperationException("Chỉ chủ nhóm được chọn người kế nhiệm.");
            await CommitGroupAction(new GroupActionBody { Kind = kind, Group = group, SuccessorId = kind == "transfer" ? successorId! : "" });
            return kind == "transfer" ? successorId : null;
        }
        finally { groupChanges.Release(); }
    }

    private async Task CommitGroupAction(GroupActionBody action)
    {
        var envelope = GroupActions.Sign(Identity, action);
        var applied = Store.ApplyGroupAction(envelope, Identity.Id);
        if (!applied.Applied) throw new InvalidOperationException("Nhóm vừa thay đổi. Hãy thử lại.");
        Changed?.Invoke();
        // Keep signed control records after hiding/deleting the chat, so other
        // trusted members can relay them while the original sender is offline.
        foreach (var peer in Peers.Where(p => p.Trusted && !p.Blocked)) Track(Replay(peer.Id));
        if (applied.Deleted.Count > 0) await DeleteStoredAttachments(applied.Deleted);
    }

    private async Task ReplayGroupActions(string peerId)
    {
        if (peers[peerId].ProtocolVersion < 4) return;
        var actions = Store.GroupActionRecords();
        var groups = actions.Where(a => a.Body.Group.Members.Contains(peerId)).Select(a => a.Body.Group.Id)
            .Concat(Store.Groups().Where(g => g.Members.Contains(peerId)).Select(g => g.Id)).ToHashSet();
        foreach (var (envelope, body) in actions.Where(a => groups.Contains(a.Body.Group.Id)))
            await SendTo(peerId, new WireMessage { Type = "group-action", Data = Codec.Encode(envelope) });
    }

    private async Task HandleGroupAction(string data)
    {
        var envelope = Codec.Decode<SignedGroupAction>(data);
        var body = GroupActions.Verify(envelope);
        await groupChanges.WaitAsync(lifetime.Token);
        (bool Applied, List<ChatMessage> Deleted) result;
        try { result = Store.ApplyGroupAction(envelope, Identity.Id); }
        finally { groupChanges.Release(); }
        if (!result.Applied) return;
        Changed?.Invoke();
        if (result.Deleted.Count > 0) await DeleteStoredAttachments(result.Deleted);
        foreach (var peer in Peers.Where(p => p.Trusted && !p.Blocked)) Track(Replay(peer.Id));
        var group = Store.Groups().Find(g => g.Id == body.Group.Id);
        if (group?.OwnerId == Identity.Id && Store.GroupActionRecords().Any(a => a.Body.Group.Id == group.Id && a.Body.Kind == "leave" && a.Body.ActorId == Identity.Id && a.Body.Group.MemberAccess.GetValueOrDefault(Identity.Id)?.JoinedRevision == group.MemberAccess.GetValueOrDefault(Identity.Id)?.JoinedRevision))
            await LeaveGroupAsync(group.Id);
    }
}
