namespace Lantern.Core;

internal static class GroupMembership
{
    public static bool CanRead(ChatGroup group, string memberId, ChatMessage message) =>
        !group.Deleted && group.Members.Contains(memberId) && message.GroupRevision <= group.Revision &&
        (!group.MemberAccess.TryGetValue(memberId, out var access) || access.IncludeHistory || message.GroupRevision >= access.JoinedRevision);

    public static bool IsHistory(ChatGroup group, string memberId, ChatMessage message) =>
        group.MemberAccess.TryGetValue(memberId, out var access) && message.GroupRevision < access.JoinedRevision;

    public static void Validate(ChatGroup group)
    {
        ProfileAvatar.Validate(group.Avatar);
        if (!Protocol.ValidId(group.Id) || string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 64 || group.Name.Any(char.IsControl) ||
            group.Revision < 0 || group.Revision == long.MaxValue || group.OwnerGeneration < 0 || group.OwnerGeneration == long.MaxValue || group.OwnershipId is null || (group.OwnerGeneration == 0 ? group.OwnershipId.Length != 0 : !Protocol.ValidId(group.OwnershipId)) || group.Members.Count is < 1 or > 32 || !group.Members.Contains(group.OwnerId) ||
            group.Members.Distinct().Count() != group.Members.Count || group.Members.Any(id => id.Length != 64 || !id.All(Uri.IsHexDigit)) ||
            group.MemberAccess.Any(p => !group.Members.Contains(p.Key) || p.Value is null || p.Value.JoinedRevision < 1 || p.Value.JoinedRevision > group.Revision))
            throw new InvalidDataException("Nhóm không hợp lệ.");
    }

    public static bool ShouldReplace(ChatGroup? current, ChatGroup incoming)
    {
        Validate(incoming);
        if (current is null) return true;
        if (current.Deleted) return false;
        if (current.OwnerId != incoming.OwnerId || current.OwnerGeneration != incoming.OwnerGeneration || current.OwnershipId != incoming.OwnershipId || incoming.Deleted) throw new InvalidDataException("Thay đổi chủ nhóm cần xác thực chuyển quyền.");
        if (incoming.Revision < current.Revision) return false;
        if (incoming.Revision == current.Revision)
        {
            if (current.ForwardAllowed != incoming.ForwardAllowed || current.CaptureProtected != incoming.CaptureProtected || current.Name != incoming.Name || current.Avatar != incoming.Avatar || !current.Members.SequenceEqual(incoming.Members) || current.MemberAccess.Count != incoming.MemberAccess.Count || current.MemberAccess.Any(p => incoming.MemberAccess.GetValueOrDefault(p.Key) != p.Value))
                throw new InvalidDataException("Phiên bản nhóm xung đột.");
            return false;
        }
        if (current.Name != incoming.Name || current.Avatar != incoming.Avatar || current.Members.Any(id => !incoming.Members.Contains(id) || current.MemberAccess.GetValueOrDefault(id) != incoming.MemberAccess.GetValueOrDefault(id)) ||
            incoming.Members.Except(current.Members).Any(id => !incoming.MemberAccess.TryGetValue(id, out var access) || access.JoinedRevision <= current.Revision))
            throw new InvalidDataException("Không được thay đổi quyền lịch sử của thành viên đã tham gia.");
        return true;
    }
}
