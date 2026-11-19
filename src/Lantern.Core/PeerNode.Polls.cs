namespace Lantern.Core;

public sealed partial class PeerNode
{
    // The creator acknowledges ballots and publishes the tally. Authenticated peers
    // can submit only their own ballot; reconnects retry a durable pending ballot.
    private readonly SemaphoreSlim pollChanges = new(1, 1);
    public async Task<ChatMessage> CreatePollAsync(string groupId, string question, IEnumerable<string> options, bool multiple = false, DateTimeOffset? endsAt = null)
    {
        var group = Store.Groups().Find(g => g.Id == groupId && !g.Deleted && g.Members.Contains(Identity.Id)) ?? throw new InvalidOperationException("Chỉ tạo bình chọn trong nhóm đang tham gia.");
        var poll = new ChatPoll { Question = question.Trim(), Options = options.Select(o => o.Trim()).ToList(), Multiple = multiple, EndsAt = endsAt };
        poll.Validate();
        if (endsAt <= DateTimeOffset.UtcNow) throw new ArgumentException("Thời hạn bình chọn phải ở tương lai.");
        var recipients = Recipients(group.Id);
        var message = new ChatMessage { ConversationId = group.Id, GroupId = group.Id, GroupRevision = group.Revision, SenderId = Identity.Id, SenderName = Identity.Name, Outgoing = true,
            Text = poll.Summary(), Poll = poll, Delivery = recipients.ToDictionary(id => id, _ => "queued"), DisappearAfterSeconds = DisappearSeconds(group.Id, recipients) };
        Store.SaveMessage(message); Changed?.Invoke();
        foreach (var id in recipients) await SendTo(id, new WireMessage { Type = "message", Message = ForWire(message) });
        return message;
    }

    public async Task VotePollAsync(string messageId, IEnumerable<int> choices)
    {
        await pollChanges.WaitAsync(lifetime.Token);
        try
        {
            var message = RequirePoll(messageId); var poll = message.Poll!;
            if (poll.IsClosed) throw new InvalidOperationException("Bình chọn đã kết thúc.");
            var ballot = new PollBallot { Choices = choices.Distinct().Order().ToArray(), Revision = checked(Math.Max(poll.Votes.GetValueOrDefault(Identity.Id)?.Revision ?? 0, message.PendingPollVote?.Revision ?? 0) + 1) };
            poll.ValidateBallot(ballot);
            if (message.SenderId == Identity.Id)
            {
                Store.UpdateMessage(messageId, m => { m.Poll!.Votes[Identity.Id] = ballot; m.Poll.Revision = checked(m.Poll.Revision + 1); });
                await PublishPoll(Store.Message(messageId)!);
            }
            else
            {
                Store.UpdateMessage(messageId, m => m.PendingPollVote = ballot);
                await SendTo(message.SenderId, new WireMessage { Type = "poll-vote", Id = messageId, Data = Codec.Encode(ballot) });
            }
            Changed?.Invoke();
        }
        finally { pollChanges.Release(); }
    }

    public async Task ClosePollAsync(string messageId)
    {
        await pollChanges.WaitAsync(lifetime.Token);
        try
        {
            var message = RequirePoll(messageId);
            if (message.SenderId != Identity.Id) throw new InvalidOperationException("Chỉ người tạo được kết thúc bình chọn.");
            Store.UpdateMessage(messageId, m => { m.Poll!.Closed = true; m.Poll.Revision = checked(m.Poll.Revision + 1); });
            Changed?.Invoke(); await PublishPoll(Store.Message(messageId)!);
        }
        finally { pollChanges.Release(); }
    }

    private ChatMessage RequirePoll(string id)
    {
        var message = Store.Message(id) ?? throw new ArgumentException("Bình chọn không còn tồn tại.");
        if (message.Poll is null || message.GroupId.Length == 0 || !Store.Groups().Any(g => g.Id == message.GroupId && !g.Deleted && g.Members.Contains(Identity.Id)))
            throw new InvalidOperationException("Bạn không còn tham gia bình chọn này.");
        return message;
    }
    private async Task PublishPoll(ChatMessage message)
    {
        foreach (var peer in Store.Groups().Find(g => g.Id == message.GroupId)?.Members.Where(id => id != Identity.Id) ?? [])
            if (ConversationHasPeer(message, peer)) await SendTo(peer, new WireMessage { Type = "poll-state", Id = message.Id, Data = Codec.Encode(message.Poll) });
    }
    private async Task ReplayPolls(string peerId)
    {
        foreach (var message in Store.Messages(limit: int.MaxValue).Where(m => m.Poll is not null && ConversationHasPeer(m, peerId)))
        {
            if (message.SenderId == Identity.Id) await SendTo(peerId, new WireMessage { Type = "poll-state", Id = message.Id, Data = Codec.Encode(message.Poll) });
            else if (message.SenderId == peerId && message.PendingPollVote is { } ballot) await SendTo(peerId, new WireMessage { Type = "poll-vote", Id = message.Id, Data = Codec.Encode(ballot) });
        }
    }
    private async Task HandlePoll(string peerId, WireMessage wire)
    {
        if (!Protocol.ValidId(wire.Id) || wire.Data.Length > 30000) throw new InvalidDataException("Gói bình chọn không hợp lệ.");
        await pollChanges.WaitAsync(lifetime.Token);
        try
        {
            var message = Store.Message(wire.Id);
            if (message?.Poll is not { } poll || !ConversationHasPeer(message, peerId)) return;
            if (wire.Type == "poll-sync")
            {
                if (message.SenderId == Identity.Id) await SendTo(peerId, new WireMessage { Type = "poll-state", Id = message.Id, Data = Codec.Encode(poll) });
                return;
            }
            if (wire.Type == "poll-vote")
            {
                if (message.SenderId != Identity.Id) return;
                var ballot = Codec.Decode<PollBallot>(wire.Data); poll.ValidateBallot(ballot);
                if (!poll.IsClosed && ballot.Revision > (poll.Votes.GetValueOrDefault(peerId)?.Revision ?? 0))
                {
                    Store.UpdateMessage(message.Id, m => { m.Poll!.Votes[peerId] = ballot; m.Poll.Revision = checked(m.Poll.Revision + 1); });
                    Changed?.Invoke(); await PublishPoll(Store.Message(message.Id)!);
                }
                else await SendTo(peerId, new WireMessage { Type = "poll-state", Id = message.Id, Data = Codec.Encode(poll) });
            }
            else if (message.SenderId == peerId)
            {
                var state = Codec.Decode<ChatPoll>(wire.Data); state.Validate();
                if (state.Question != poll.Question || !state.Options.SequenceEqual(poll.Options) || state.Multiple != poll.Multiple || state.EndsAt != poll.EndsAt || state.Revision < poll.Revision || poll.Closed && !state.Closed) return;
                Store.UpdateMessage(message.Id, m =>
                {
                    m.Poll = state;
                    if (state.IsClosed || (state.Votes.GetValueOrDefault(Identity.Id)?.Revision ?? 0) >= (m.PendingPollVote?.Revision ?? long.MaxValue)) m.PendingPollVote = null;
                });
                Changed?.Invoke();
            }
        }
        finally { pollChanges.Release(); }
    }
}
