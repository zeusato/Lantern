using Lantern.Core;

internal static class GroupMembershipTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var identities = Enumerable.Range(0, 5).Select(i => new Identity(Path.Combine(root, "group-members-" + i), "Member " + i)).ToArray();
        var stores = Enumerable.Range(0, 5).Select(i => new LocalStore(Path.Combine(root, "group-members-" + i))).ToArray();
        var nodes = Enumerable.Range(0, 5).Select(i => new PeerNode(identities[i], stores[i])).ToArray();
        async Task Until(Func<bool> ready, string name)
        {
            var end = DateTime.UtcNow.AddSeconds(12);
            while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException(name); await Task.Delay(40); }
        }
        try
        {
            foreach (var node in nodes) await node.StartAsync(0, false, true);
            for (int a = 0; a < nodes.Length; a++)
                for (int b = a + 1; b < nodes.Length; b++)
                {
                    await nodes[a].ConnectAsync("127.0.0.1", nodes[b].Port, identities[b].Id);
                    await Until(() => nodes[b].IsConnected(identities[a].Id), "Group fixture pairing");
                    await nodes[a].TrustAsync(identities[b].Id); await nodes[b].TrustAsync(identities[a].Id);
                }
            bool invalidAvatar = false;
            try { await nodes[0].CreateGroupAsync("Bad photo", [identities[1].Id, identities[2].Id], "invalid"); } catch (InvalidDataException) { invalidAvatar = true; }
            check(invalidAvatar && stores[0].Groups().Count == 0, "Invalid group photos are rejected before creating a group");
            var group = await nodes[0].CreateGroupAsync("Growing group", [identities[1].Id, identities[2].Id], GroupAvatarFixture.Image);
            await Until(() => stores[1].Groups().Count == 1 && stores[2].Groups().Count == 1, "Initial group");
            check(stores.Take(3).All(store => store.Groups().Single().Avatar == GroupAvatarFixture.Image), "Group photos synchronize intact to original members");
            var ownerOld = await nodes[0].SendTextAsync(group.Id, "Owner's old message");
            var authorOld = await nodes[1].SendTextAsync(group.Id, "Another author's old message");
            var file = Path.Combine(root, "group-history.txt"); await File.WriteAllTextAsync(file, "group attachment");
            var attachment = await nodes[1].OfferFileAsync(group.Id, file);
            await Until(() => stores[2].Message(authorOld.Id) is not null, "Initial group chat");
            bool rejected = false;
            try { await nodes[1].AddGroupMembersAsync(group.Id, [identities[3].Id], true); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && stores[1].Groups().Single().Members.Count == 3, "Only the group owner can add members");
            nodes[0].FindPeer(identities[3].Id)!.ProtocolVersion = 2;
            rejected = false;
            try { await nodes[0].AddGroupMembersAsync(group.Id, [identities[3].Id], false); } catch (InvalidOperationException) { rejected = true; }
            nodes[0].FindPeer(identities[3].Id)!.ProtocolVersion = 3;
            check(rejected && stores[0].Groups().Single().Revision == 0, "Unsupported old clients are rejected before mutating group membership");

            await nodes[0].AddGroupMembersAsync(group.Id, [identities[3].Id], false);
            await Until(() => stores.Take(4).All(s => s.Groups().Single().Revision == 1), "No-history member roster");
            var fresh = await nodes[2].SendTextAsync(group.Id, "After joining");
            await Until(() => stores[3].Message(fresh.Id) is not null, "New member receives live chat");
            check(stores[3].Message(ownerOld.Id) is null && stores[3].Message(authorOld.Id) is null && stores[3].Message(attachment.Id) is null, "New member with history disabled receives new messages but no old text or attachments");
            check(!stores[0].Message(ownerOld.Id)!.Delivery.ContainsKey(identities[3].Id) && !stores[1].Message(authorOld.Id)!.Delivery.ContainsKey(identities[3].Id), "History disabled is enforced at each author's outgoing queue");
            // Even a stale queued entry must not leak an old message on reconnect.
            stores[0].UpdateMessage(ownerOld.Id, m => m.Delivery[identities[3].Id] = "queued");
            await nodes[0].TrustAsync(identities[3].Id);
            var marker = await nodes[0].SendTextAsync(group.Id, "Replay barrier");
            await Until(() => stores[3].Message(marker.Id) is not null, "Replay barrier received");
            check(stores[3].Message(ownerOld.Id) is null, "Reconnect replay cannot bypass a member's history restriction");

            await nodes[0].AddGroupMembersAsync(group.Id, [identities[4].Id], true);
            await Until(() => stores.All(s => s.Groups().Single().Revision == 2), "History member roster");
            await Until(() => stores[4].Message(ownerOld.Id) is not null && stores[4].Message(authorOld.Id) is not null && stores[4].Message(attachment.Id) is not null, "History from original authors");
            check(stores[4].Message(authorOld.Id)!.SenderId == identities[1].Id && stores[4].UnreadCount() == 0, "Allowed history preserves authorship without new-message notifications or unread counts");
            await nodes[4].AcceptFileAsync(attachment.Id);
            await Until(() => stores[4].Message(attachment.Id)?.Status == "received", "Historical attachment transfer");
            check(await File.ReadAllTextAsync(stores[4].Message(attachment.Id)!.FilePath) == "group attachment", "Allowed historical attachments remain downloadable from their original sender");
            await nodes[4].DeleteMessagesAsync(group.Id, new HashSet<string> { authorOld.Id });
            stores[1].UpdateMessage(authorOld.Id, m => m.Delivery[identities[4].Id] = "queued"); await nodes[1].TrustAsync(identities[4].Id);
            marker = await nodes[1].SendTextAsync(group.Id, "Deletion replay barrier");
            await Until(() => stores[4].Message(marker.Id) is not null, "Deletion replay barrier received");
            check(stores[4].Message(authorOld.Id) is null, "History synchronization honors local deletion tombstones");

            using var reopened = new LocalStore(stores[3].DirectoryPath);
            var persisted = reopened.Groups().Single();
            check(persisted.Avatar == GroupAvatarFixture.Image && stores.All(store => store.Groups().Single().Avatar == GroupAvatarFixture.Image), "Group photos persist after restart and synchronize to later members");
            check(!persisted.MemberAccess[identities[3].Id].IncludeHistory && persisted.MemberAccess[identities[4].Id].IncludeHistory, "Per-member history choices persist and survive later additions");
            var stale = Codec.Decode<ChatGroup>(Codec.Encode(persisted)); stale.Revision = 0; stale.Members = group.Members; stale.MemberAccess.Clear();
            check(!reopened.ApplyGroup(stale) && reopened.Groups().Single().Members.Count == 5, "Stale roster replay cannot remove added members");
            var tampered = Codec.Decode<ChatGroup>(Codec.Encode(persisted)); tampered.Revision++; tampered.MemberAccess[identities[3].Id] = new(1, true);
            rejected = false; try { reopened.ApplyGroup(tampered); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Later roster updates cannot silently grant history to a restricted member");
            var changedPhoto = Codec.Decode<ChatGroup>(Codec.Encode(persisted)); changedPhoto.Avatar = "";
            rejected = false; try { reopened.ApplyGroup(changedPhoto); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Conflicting rosters cannot silently replace a group's photo");
        }
        finally
        {
            foreach (var node in nodes) await node.DisposeAsync();
            foreach (var store in stores) store.Dispose(); foreach (var identity in identities) identity.Dispose();
        }
    }
}
