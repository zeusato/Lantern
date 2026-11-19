using System.Reflection;
using Lantern.Core;

internal static class RichPollTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var rich = new RichTextContent { Paragraphs = [new() { Runs = [new() { Text = "Tiếng Việt ", Bold = true }, new() { Text = "🙂 liên kết", Italic = true, Underline = true, Strike = true, Size = 24, Color = "#408BE5" }] }, new() { List = "number", Indent = 2, Runs = [new() { Text = "Mục thứ hai" }] }] };
        var text = rich.PlainText(); rich.Validate(text);
        check(Codec.Decode<RichTextContent>(Codec.Encode(rich)).PlainText() == text, "Rich text preserves Unicode, paragraphs and fallback text");
        void Invalid(Action action, string label) { bool rejected = false; try { action(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or InvalidOperationException) { rejected = true; } check(rejected, label); }
        Invalid(() => rich.Validate(text + "fake"), "Rich text cannot disagree with searchable fallback text");
        var malformed = Codec.Decode<RichTextContent>(Codec.Encode(rich)); malformed.Paragraphs[0].Runs[0].Color = "url(file:///private)";
        Invalid(() => malformed.Validate(text), "Rich text rejects arbitrary colors and external resources");
        malformed.Paragraphs[0].Runs[0].Color = ""; malformed.Paragraphs[0].Runs[0].Size = int.MaxValue;
        Invalid(() => malformed.Validate(text), "Rich text rejects unbounded font sizes");
        Invalid(() => Codec.Decode<RichTextContent>("{\"Paragraphs\":[null]}").Validate(""), "Null rich paragraphs are rejected");
        Invalid(() => new ChatPoll { Question = "Chọn?", Options = ["A", "a"] }.Validate(), "Duplicate poll options are rejected");

        var identities = Enumerable.Range(0, 3).Select(i => new Identity(Path.Combine(root, "peer" + i), "Rich peer " + i)).ToArray();
        var stores = Enumerable.Range(0, 3).Select(i => new LocalStore(Path.Combine(root, "peer" + i))).ToArray();
        var nodes = Enumerable.Range(0, 3).Select(i => new PeerNode(identities[i], stores[i])).ToArray();
        async Task Until(Func<bool> ready, string label)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!ready()) { if (DateTime.UtcNow > until) throw new TimeoutException(label); await Task.Delay(40); }
        }
        async Task Rejected(Func<Task> action, string label)
        {
            bool rejected = false; try { await action(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidDataException) { rejected = true; } check(rejected, label);
        }
        try
        {
            foreach (var node in nodes) await node.StartAsync(0, false, true);
            for (int a = 0; a < 3; a++) for (int b = a + 1; b < 3; b++)
            {
                await nodes[a].ConnectAsync("127.0.0.1", nodes[b].Port, identities[b].Id);
                await Until(() => nodes[b].IsConnected(identities[a].Id), "connect");
                await nodes[a].TrustAsync(identities[b].Id); await nodes[b].TrustAsync(identities[a].Id);
            }
            var sent = await nodes[0].SendTextAsync(identities[1].Id, text, richText: rich);
            await Until(() => stores[1].Message(sent.Id) is not null, "rich receipt");
            check(Codec.Encode(stores[1].Message(sent.Id)!.RichText) == Codec.Encode(rich), "TLS delivery preserves all rich text styles");
            var forwarded = await nodes[1].ForwardMessageAsync(sent.Id, identities[2].Id);
            await Until(() => stores[2].Message(forwarded.Id) is not null, "rich forwarding");
            check(Codec.Encode(stores[2].Message(forwarded.Id)!.RichText) == Codec.Encode(rich), "Forwarding preserves rich text");
            var group = await nodes[0].CreateGroupAsync("Bình chọn", [identities[1].Id, identities[2].Id]);
            await Until(() => stores.Skip(1).All(s => s.Groups().Any(g => g.Id == group.Id)), "group receipt");
            await Rejected(() => nodes[0].CreatePollAsync(identities[1].Id, "No", ["A", "B"]), "Polls cannot be created in direct chats");
            var pollMessage = await nodes[0].CreatePollAsync(group.Id, "Chọn ngày họp?", ["Thứ hai", "Thứ ba"], true);
            await Until(() => stores.Skip(1).All(s => s.Message(pollMessage.Id)?.Poll is not null), "poll receipt");
            var id = pollMessage.Id;
            await nodes[1].VotePollAsync(id, [0, 1]);
            await Until(() => stores.All(s => s.Message(id)!.Poll!.Votes.GetValueOrDefault(identities[1].Id)?.Choices.Length == 2) && stores[1].Message(id)!.PendingPollVote is null, "vote acknowledgement");
            check(true, "Multiple-choice ballots and acknowledgements converge on all group members");
            await nodes[1].VotePollAsync(id, [1]);
            await Until(() => stores[2].Message(id)!.Poll!.Votes[identities[1].Id].Choices.SequenceEqual([1]), "changed vote");
            check(stores[0].Message(id)!.Poll!.Votes.Count == 1, "Changing a ballot does not count the same voter twice");
            await nodes[1].VotePollAsync(id, []);
            await Until(() => stores[0].Message(id)!.Poll!.Votes[identities[1].Id].Choices.Length == 0, "withdraw vote");
            check(true, "Voters can withdraw their ballot");
            var before = Codec.Encode(stores[2].Message(id)!.Poll);
            var forged = Codec.Decode<ChatPoll>(before); forged.Closed = true; forged.Revision += 20;
            await (Task)typeof(PeerNode).GetMethod("HandlePoll", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(nodes[2], [identities[1].Id, new WireMessage { Type = "poll-state", Id = id, Data = Codec.Encode(forged) }])!;
            check(!stores[2].Message(id)!.Poll!.Closed, "A member cannot forge a creator tally or close the poll");
            await Rejected(() => nodes[1].ClosePollAsync(id), "Only the poll creator can close it");
            await nodes[0].DisposeAsync();
            await Until(() => !nodes[1].IsConnected(identities[0].Id), "creator disconnect");
            await nodes[1].VotePollAsync(id, [0]);
            check(stores[1].Message(id)!.PendingPollVote?.Choices.SequenceEqual([0]) == true, "Offline ballots are stored durably as pending");
            nodes[0] = new PeerNode(identities[0], stores[0]); await nodes[0].StartAsync(0, false, true);
            for (int i = 1; i < 3; i++) await nodes[i].ConnectAsync("127.0.0.1", nodes[0].Port, identities[0].Id);
            await Until(() => stores.All(s => s.Message(id)!.Poll!.Votes[identities[1].Id].Choices.SequenceEqual([0])) && stores[1].Message(id)!.PendingPollVote is null, "offline replay");
            check(true, "Reconnecting the creator acknowledges pending votes and converges results");
            await nodes[0].ClosePollAsync(id);
            await Until(() => stores.All(s => s.Message(id)!.Poll!.Closed), "close receipt");
            await Rejected(() => nodes[2].VotePollAsync(id, [1]), "Closed polls reject new votes");
            var single = await nodes[0].CreatePollAsync(group.Id, "Một lựa chọn", ["A", "B"]);
            await Rejected(() => nodes[0].VotePollAsync(single.Id, [0, 1]), "Single-choice polls reject multiple selections");
            await Rejected(() => nodes[0].VotePollAsync(single.Id, [10]), "Out-of-range ballot choices are rejected");
            var expired = await nodes[0].CreatePollAsync(group.Id, "Hết giờ", ["A", "B"], endsAt: DateTimeOffset.UtcNow.AddMilliseconds(100));
            await Task.Delay(150);
            await Rejected(() => nodes[0].VotePollAsync(expired.Id, [0]), "Poll deadlines reject late votes");
            using var reopened = new LocalStore(Path.Combine(root, "peer0"));
            check(reopened.Message(id)!.Poll!.Closed && reopened.Message(sent.Id)!.RichText!.PlainText() == text, "Rich text and poll results survive reopening persisted history");
        }
        finally
        {
            foreach (var node in nodes) await node.DisposeAsync();
            foreach (var store in stores) store.Dispose();
            foreach (var identity in identities) identity.Dispose();
        }
    }
}
