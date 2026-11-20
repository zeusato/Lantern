namespace Lantern.Core;

public sealed partial class PeerNode
{
    public ConversationRestrictions ConversationRules(string id)
    {
        var group = Store.Groups().Find(g => g.Id == id);
        var policy = group is null ? Store.Restrictions().Find(p => p.Id == id) ?? new() { Id = id } : new() { Id = id, Actor = group.OwnerId, Revision = group.Revision, CaptureProtected = group.CaptureProtected, ForwardAllowed = group.ForwardAllowed }; policy.NormalizeOwnership(); return policy;
    }
    public async Task SetConversationRulesAsync(string id, bool captureProtected, bool forwardAllowed)
    {
        await groupChanges.WaitAsync(lifetime.Token);
        try
        {
            var group = Store.Groups().Find(g => g.Id == id);
            if (group is not null)
            {
                if (group.OwnerId != Identity.Id) throw new InvalidOperationException("Chỉ chủ nhóm được đổi quyền của nhóm.");
                RequireGroupCapability(group, 5);
                group.Revision++; group.CaptureProtected = captureProtected; group.ForwardAllowed = forwardAllowed;
                Store.ApplyGroup(group);
                foreach (var member in group.Members.Where(m => m != Identity.Id)) await SendTo(member, new WireMessage { Type = "group", Group = group });
            }
            else
            {
                if (FindPeer(id) is not { Trusted: true, Blocked: false, ProtocolVersion: >= 7 }) throw new InvalidOperationException("Đối tác cần ghép đôi và cập nhật Lantern để đồng bộ quyền của hội thoại.");
                var policy = ConversationRules(id);
                var claim = policy.Claims.GetValueOrDefault(Identity.Id) ?? new RestrictionClaim();
                bool changeCapture = captureProtected != policy.CaptureProtected, changeForward = forwardAllowed != policy.ForwardAllowed;
                if (changeCapture && policy.CaptureProtected && !policy.CaptureOwners.Contains(Identity.Id) ||
                    changeForward && !policy.ForwardAllowed && !policy.ForwardOwners.Contains(Identity.Id))
                    throw new InvalidOperationException("Chỉ người đã thiết lập quyền này mới được trả về mặc định.");
                if (!changeCapture && !changeForward) return;
                claim = new() { Revision = checked(claim.Revision + 1), CaptureProtected = changeCapture ? captureProtected : claim.CaptureProtected, ForwardAllowed = changeForward ? forwardAllowed : claim.ForwardAllowed };
                Store.MergeRestrictionClaim(id, Identity.Id, claim); await SendRestrictions(id, ConversationRules(id));
            }
            Changed?.Invoke();
        }
        finally { groupChanges.Release(); }
    }
    public bool CanChangeConversationRule(string id, bool capture)
    {
        var group = Store.Groups().Find(g => g.Id == id);
        if (group is not null) return group.OwnerId == Identity.Id;
        var rules = ConversationRules(id); var owners = capture ? rules.CaptureOwners : rules.ForwardOwners;
        return owners.Length == 0 || owners.Contains(Identity.Id);
    }
    private Task SendRestrictions(string peerId, ConversationRestrictions policy)
    {
        policy.NormalizeOwnership();
        if (!policy.Claims.TryGetValue(Identity.Id, out var claim)) return Task.CompletedTask;
        return SendTo(peerId, new WireMessage { Type = "conversation-rules", Data = Codec.Encode(new ConversationRestrictions { Actor = Identity.Id, Revision = claim.Revision, OwnershipVersion = 2, CaptureProtected = claim.CaptureProtected, ForwardAllowed = claim.ForwardAllowed }) });
    }
    private async Task ReceiveRestrictions(string peerId, string data)
    {
        var incoming = Codec.Decode<ConversationRestrictions>(data);
        if (incoming.Id != "" || incoming.Actor != peerId || incoming.Revision < 1 || incoming.Revision == long.MaxValue)
            throw new InvalidDataException("Quyền hội thoại không hợp lệ.");
        await groupChanges.WaitAsync(lifetime.Token);
        try
        {
            var current = ConversationRules(peerId);
            // A legacy whole-policy update cannot release another participant's restriction.
            if (incoming.OwnershipVersion < 2 &&
                (current.CaptureProtected && !incoming.CaptureProtected && current.CaptureOwners.Any(id => id != peerId) ||
                 !current.ForwardAllowed && incoming.ForwardAllowed && current.ForwardOwners.Any(id => id != peerId))) return;
            if (Store.MergeRestrictionClaim(peerId, peerId, new() { Revision = incoming.Revision, CaptureProtected = incoming.CaptureProtected, ForwardAllowed = incoming.ForwardAllowed })) Changed?.Invoke();
        }
        finally { groupChanges.Release(); }
    }
    public async Task<ChatMessage> ForwardMessageAsync(string messageId, string target, CancellationToken ct = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token); ct = stop.Token;
        var original = Store.Message(messageId) ?? throw new ArgumentException("Tin nhắn không còn tồn tại.");
        if (!ConversationRules(original.ConversationId).ForwardAllowed) throw new InvalidOperationException("Hội thoại này không cho phép chuyển tiếp.");
        if (target == original.ConversationId) throw new ArgumentException("Chọn một hội thoại khác.");
        var recipients = Recipients(target);
        if (original.Kind == "text") return await SendTextAsync(target, original.Text, forwarded: true, richText: original.RichText);
        if (original.Kind == "contact")
        {
            ValidateContact(original.Contact ?? throw new InvalidDataException("Liên hệ không hợp lệ."));
            var message = new ChatMessage { ConversationId = target, SenderId = Identity.Id, SenderName = Identity.Name, Kind = "contact", Contact = original.Contact, Outgoing = true, Forwarded = true, GroupId = Store.Groups().Any(g => g.Id == target) ? target : "", GroupRevision = GroupRevision(target), Delivery = recipients.ToDictionary(id => id, _ => "queued"), DisappearAfterSeconds = DisappearSeconds(target, recipients) };
            Store.SaveMessage(message); Changed?.Invoke();
            foreach (var peerId in recipients) await SendTo(peerId, new WireMessage { Type = "contact", Message = ForWire(message) });
            return message;
        }
        if (!File.Exists(original.FilePath))
        {
            if (original.Outgoing) throw new IOException("Bản tệp không còn trên máy.");
            await AcceptFileAsync(original.Id);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                original = Store.Message(messageId) ?? throw new IOException("Tệp đã bị xóa hoặc hết hạn.");
                if (original.Status == "received" && File.Exists(original.FilePath)) break;
                if (original.Status is "error" or "declined" || !IsConnected(original.SenderId)) throw new IOException("Chưa tải được tệp để chuyển tiếp. Hãy thử khi người gửi online.");
                await Task.Delay(100, ct);
            }
        }
        if (!ConversationRules(original.ConversationId).ForwardAllowed) throw new InvalidOperationException("Hội thoại vừa tắt quyền chuyển tiếp.");
        return await OfferFileAsync(target, original.FilePath, ct, displayFileName: original.FileName, forwarded: true, forwardSource: original.Id, sticker: original.Sticker);
    }
}
