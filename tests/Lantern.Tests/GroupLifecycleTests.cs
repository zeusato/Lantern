using Lantern.Core;

internal static class GroupLifecycleTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var identities = Enumerable.Range(0, 4).Select(i => new Identity(Path.Combine(root, "lifecycle-" + i), "Lifecycle " + i)).ToArray();
        var stores = Enumerable.Range(0, 4).Select(i => new LocalStore(Path.Combine(root, "lifecycle-" + i))).ToArray();
        var nodes = Enumerable.Range(0, 4).Select(i => new PeerNode(identities[i], stores[i])).ToArray();
        async Task Until(Func<bool> ready, string name)
        {
            var end = DateTime.UtcNow.AddSeconds(12);
            while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException(name); await Task.Delay(35); }
        }
        try
        {
            foreach (var node in nodes) await node.StartAsync(0, false, true);
            for (int a = 0; a < 4; a++) for (int b = a + 1; b < 4; b++)
            {
                await nodes[a].ConnectAsync("127.0.0.1", nodes[b].Port, identities[b].Id);
                await Until(() => nodes[b].IsConnected(identities[a].Id), "Lifecycle fixture connection");
                await nodes[a].TrustAsync(identities[b].Id); await nodes[b].TrustAsync(identities[a].Id);
            }
            var group = await nodes[0].CreateGroupAsync("Lifecycle group", identities.Skip(1).Select(i => i.Id), GroupAvatarFixture.Image);
            await Until(() => stores.All(s => s.Groups().Count == 1), "Initial lifecycle roster");
            var message = await nodes[0].SendTextAsync(group.Id, "history retained on leave");
            await Until(() => stores.All(s => s.Message(message.Id) is not null), "Lifecycle initial history");
            bool denied = false;
            try { await nodes[1].DeleteGroupAsync(group.Id); } catch (InvalidOperationException) { denied = true; }
            check(denied && stores.All(s => s.Groups().Count == 1), "Only the current owner can delete a group");
            await nodes[0].SetConversationRulesAsync(group.Id, true, false);
            await Until(() => nodes.All(n => n.ConversationRules(group.Id) is { CaptureProtected: true, ForwardAllowed: false }), "Group rule sync");
            denied = false; try { await nodes[1].SetConversationRulesAsync(group.Id, false, true); } catch (InvalidOperationException) { denied = true; }
            check(denied, "Only the current group owner can change shared capture and forwarding permissions");
            denied = false; try { await nodes[1].ForwardMessageAsync(message.Id, identities[2].Id); } catch (InvalidOperationException) { denied = true; }
            check(denied, "Forwarding from a restricted group is rejected in the core API");
            await nodes[0].SetConversationRulesAsync(group.Id, false, true);
            await Until(() => nodes.All(n => n.ConversationRules(group.Id).ForwardAllowed), "Forward permission enabled");
            var forwarded = await nodes[1].ForwardMessageAsync(message.Id, identities[2].Id);
            await Until(() => stores[2].Message(forwarded.Id) is not null, "Forwarded delivery");
            check(stores[2].Message(forwarded.Id) is { Forwarded: true } delivered && delivered.SenderId == identities[1].Id && delivered.Text == message.Text && delivered.Id != message.Id, "Forwarding creates a distinct message from the forwarding user");
            await nodes[0].SetConversationRulesAsync(identities[1].Id, true, false);
            await Until(() => nodes[1].ConversationRules(identities[0].Id) is { CaptureProtected: true, ForwardAllowed: false }, "Direct rule sync");
            check(true, "Direct conversation permissions synchronize to the partner");
            await nodes[1].SetConversationRulesAsync(identities[0].Id, false, true);
            await Until(() => nodes[0].ConversationRules(identities[1].Id) is { CaptureProtected: false, ForwardAllowed: true }, "Direct rule update by partner");
            var file = Path.Combine(root, "forward-fixture.html"); await File.WriteAllTextAsync(file, "<!doctype html><h1>Forward fixture</h1>");
            var offer = await nodes[0].OfferFileAsync(group.Id, file);
            await Until(() => stores[1].Message(offer.Id) is not null, "Forward source offer");
            var forwardedFile = await nodes[1].ForwardMessageAsync(offer.Id, identities[2].Id);
            await Until(() => stores[2].Message(forwardedFile.Id) is not null, "Forwarded attachment offer");
            await nodes[2].AcceptFileAsync(forwardedFile.Id);
            await Until(() => stores[2].Message(forwardedFile.Id)?.Status == "received", "Forwarded attachment received");
            check(forwardedFile.Forwarded && forwardedFile.FileName == "forward-fixture.html" && forwardedFile.Hash == offer.Hash && await File.ReadAllTextAsync(stores[2].Message(forwardedFile.Id)!.FilePath) == await File.ReadAllTextAsync(file), "Forwarding downloads an unreceived attachment and sends an independent verified copy with its original filename");
            var contact = await nodes[0].ShareContactAsync(group.Id, identities[3].Id);
            await Until(() => stores[1].Message(contact.Id) is not null, "Forward source contact");
            var forwardedContact = await nodes[1].ForwardMessageAsync(contact.Id, identities[2].Id);
            await Until(() => stores[2].Message(forwardedContact.Id) is not null, "Forwarded contact received");
            check(stores[2].Message(forwardedContact.Id) is { Kind: "contact", Forwarded: true } shared && shared.Contact?.Id == identities[3].Id, "Forwarded contact cards retain public identity without author impersonation");
            await nodes[3].DisposeAsync();
            await Until(() => !nodes[0].IsConnected(identities[3].Id), "Offline group member");
            await nodes[1].LeaveGroupAsync(group.Id);
            await Until(() => stores.Take(3).All(s => !s.Groups().Single().Members.Contains(identities[1].Id)), "Member departure propagation");
            check(stores[1].Message(message.Id) is not null && stores[1].Groups().Single().OwnerId == identities[0].Id, "Leaving keeps existing local history and preserves the group owner");
            denied = false; try { await nodes[1].SendTextAsync(group.Id, "must not send"); } catch (InvalidOperationException) { denied = true; }
            check(denied, "Departed members cannot send new group messages");
            var next = await nodes[0].LeaveGroupAsync(group.Id, identities[2].Id);
            await Until(() => stores.Take(3).All(s => s.Groups().Single().OwnerId == identities[2].Id), "Designated owner transfer");
            check(next == identities[2].Id && stores[2].Groups().Single().Members.SequenceEqual(new[] { identities[2].Id, identities[3].Id }), "Owner departure transfers authority to the designated remaining member");
            check(stores.Take(3).All(store => store.Groups().Single().Avatar == GroupAvatarFixture.Image), "Large group photos survive signed departure and owner-transfer messages");
            denied = false; try { await nodes[0].DeleteGroupAsync(group.Id); } catch (InvalidOperationException) { denied = true; }
            check(denied, "Former owners lose deletion authority immediately");
            var retainedTransfer = stores[1].GroupActionRecords().Single(a => a.Body.Kind == "transfer").Envelope;
            denied = false; try { GroupActions.Verify(retainedTransfer with { Payload = retainedTransfer.Payload.Replace(identities[2].Id, identities[1].Id) }); } catch (InvalidDataException) { denied = true; }
            check(denied, "Relayed ownership transfers reject signature tampering");
            await nodes[2].DeleteGroupAsync(group.Id);
            await Until(() => stores.Take(3).All(s => s.Groups().Count == 0 && s.Messages(group.Id).Count == 0), "Global group deletion");
            check(stores.Take(3).All(s => s.UnreadCounts().GetValueOrDefault(group.Id) == 0), "Owner deletion purges the group and unread history, including members who left");
            await Until(() => !File.Exists(offer.FilePath) && !File.Exists(stores[1].MediaPath + Path.DirectorySeparatorChar + offer.Id + "_" + offer.FileName), "Deleted group attachment cleanup");
            check(File.Exists(file) && File.Exists(forwardedFile.FilePath), "Group deletion removes its owned attachments while preserving originals and independent forwarded copies");
            await nodes[2].DisposeAsync();
            nodes[3] = new PeerNode(identities[3], stores[3]); await nodes[3].StartAsync(0, false, true);
            await nodes[3].ConnectAsync("127.0.0.1", nodes[1].Port, identities[1].Id);
            await Until(() => stores[3].Groups().Count == 0 && stores[3].Messages(group.Id).Count == 0, "Offline deletion via another member");
            check(true, "Offline members receive the signed handoff and deletion through a relay while the owner is offline");
            stores[3].SaveMessage(message);
            check(stores[3].Message(message.Id) is null && !stores[3].ApplyGroup(group), "Deleted-group tombstones prevent late messages and stale rosters from resurrecting chat");

            // A separate fixture exercises random and last-member handoff behavior.
            nodes[2] = new PeerNode(identities[2], stores[2]); await nodes[2].StartAsync(0, false, true);
            await nodes[0].ConnectAsync("127.0.0.1", nodes[2].Port, identities[2].Id);
            var randomGroup = await nodes[0].CreateGroupAsync("Random succession", [identities[1].Id, identities[2].Id]);
            await Until(() => stores[1].Groups().Any(g => g.Id == randomGroup.Id) && stores[2].Groups().Any(g => g.Id == randomGroup.Id), "Random group roster");
            var chosen = await nodes[0].LeaveGroupAsync(randomGroup.Id);
            check(new[] { identities[1].Id, identities[2].Id }.Contains(chosen), "Without a selection, owner succession chooses one of the remaining members");
            await Until(() => stores.Take(3).All(s => s.Groups().Single().OwnerId == chosen), "Random owner converges");
            int ownerIndex = Array.FindIndex(identities, i => i.Id == chosen), memberIndex = ownerIndex == 1 ? 2 : 1;
            await nodes[memberIndex].LeaveGroupAsync(randomGroup.Id);
            await Until(() => stores[ownerIndex].Groups().Single().Members.Count == 1, "Last member roster");
            await nodes[ownerIndex].LeaveGroupAsync(randomGroup.Id);
            await Until(() => stores.Take(3).All(s => s.Groups().Count == 0), "Last owner leaves");
            check(true, "The last member leaving deletes the empty group everywhere");
        }
        finally
        {
            foreach (var node in nodes) { try { await node.DisposeAsync(); } catch (ObjectDisposedException) { } }
            foreach (var store in stores) store.Dispose(); foreach (var identity in identities) identity.Dispose();
        }
    }
}
