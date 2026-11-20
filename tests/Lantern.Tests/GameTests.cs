using Lantern.Core;

internal static class GameTests
{
    private static async Task Until(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(12);
        while (!predicate()) { if (DateTime.UtcNow > until) throw new TimeoutException("Game state did not converge"); await Task.Delay(30); }
    }
    public static async Task Run(string root, Action<bool, string> check)
    {
        using var aId = new Identity(Path.Combine(root, "a"), "An"); using var bId = new Identity(Path.Combine(root, "b"), "Bình");
        using var aStore = new LocalStore(Path.Combine(root, "a")); using var bStore = new LocalStore(Path.Combine(root, "b"));
        var a = new PeerNode(aId, aStore); await using var b = new PeerNode(bId, bStore);
        await a.StartAsync(0, false, true); await b.StartAsync(0, false, true); await a.ConnectAsync("127.0.0.1", b.Port); await Until(() => b.IsConnected(aId.Id)); await a.TrustAsync(bId.Id); await b.TrustAsync(aId.Id);
        async Task Ready(string id) => await Until(() => a.CurrentGame(bId.Id, id) is { Pending: false } av && b.CurrentGame(aId.Id, id) is { Pending: false } bv && av.State.Head == bv.State.Head);
        var caro = await a.InviteGameAsync(bId.Id, "caro"); await Ready(caro);
        check(b.CurrentGame(aId.Id)!.State.Status == "invited" && bStore.Messages().Single(m => m.Id == caro).GameId == caro, "Game invitation appears as a persistent chat card without starting the match");
        bool prevented = false; try { await a.InviteGameAsync(bId.Id, "words"); } catch (InvalidOperationException) { prevented = true; }
        check(prevented, "Only one invitation or active match is allowed per private conversation");
        await b.GameActionAsync(caro, "accept"); await Ready(caro);
        prevented = false; try { await b.GameActionAsync(caro, "move", 20); } catch (InvalidOperationException) { prevented = true; }
        check(prevented, "Game rejects moving out of turn before queuing network traffic");
        for (int i = 0; i < 5; i++) { await a.GameActionAsync(caro, "move", i); await Ready(caro); if (i < 4) { await b.GameActionAsync(caro, "move", 30 + i); await Ready(caro); } }
        check(a.CurrentGame(bId.Id)!.State.Winner == aId.Id && b.CurrentGame(aId.Id)!.State.WinningCells.Count == 5, "Caro detects five in a row and both peers agree on the winner");
        var signed = aStore.Game(caro)!.Events.Last(); var forged = signed with { Payload = signed.Payload.Replace("\"Cell\":4", "\"Cell\":9") }; bool rejected = false;
        try { forged.Verify(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Game moves cannot be altered without invalidating the player's signature");

        var words = await b.InviteGameAsync(aId.Id, "words"); await Ready(words); await a.GameActionAsync(words, "accept"); await Ready(words);
        foreach (var item in new[] { (b, "HỌC   SINH"), (a, "sinh nhật"), (b, "nhật ký"), (a, "ký ức") }) { await item.Item1.GameActionAsync(words, "word", text: item.Item2); await Ready(words); }
        check(a.CurrentGame(bId.Id)!.State.Words.SequenceEqual(new[] { "học sinh", "sinh nhật", "nhật ký", "ký ức" }), "Word chain normalizes case and whitespace and locks the preceding final syllable");
        prevented = false; try { await b.GameActionAsync(words, "word", text: "sinh viên"); } catch (InvalidOperationException) { prevented = true; }
        check(prevented, "Word chain rejects a mismatched starting syllable");
        await b.GameActionAsync(words, "word-propose", text: "ức lạ"); await Ready(words); await a.GameActionAsync(words, "word-reject"); await Ready(words);
        check(a.CurrentGame(bId.Id)!.State.Words.Count == 4 && a.CurrentGame(bId.Id)!.State.Turn == bId.Id, "Rejecting a proposed word keeps the original player's turn and chain unchanged");
        await b.GameActionAsync(words, "word-propose", text: "ức lạ"); await Ready(words); await a.GameActionAsync(words, "word-approve"); await Ready(words);
        check(a.CurrentGame(bId.Id)!.State.Words.Last() == "ức lạ" && a.CurrentGame(bId.Id)!.State.Turn == aId.Id, "An unlisted word only counts after the opponent explicitly approves it");
        await a.GameActionAsync(words, "draw"); await Ready(words); await b.GameActionAsync(words, "draw"); await Ready(words);
        check(a.CurrentGame(bId.Id)!.State is { Status: "finished", Winner: "" }, "Mutual draw ends the match without awarding a win");

        var ships = await a.InviteGameAsync(bId.Id, "ships"); await Ready(ships); await b.GameActionAsync(ships, "accept"); await Ready(ships);
        var aBoard = GameFleet.RandomBoard(); var bBoard = GameFleet.RandomBoard(); GameFleet.Validate(aBoard); GameFleet.Validate(bBoard);
        await a.GameActionAsync(ships, "fleet", board: aBoard); await Ready(ships); await b.GameActionAsync(ships, "fleet", board: bBoard); await Ready(ships);
        check(a.CurrentGame(bId.Id)!.State.Status == "playing" && a.CurrentGame(bId.Id)!.OwnBoard.SequenceEqual(aBoard) && !bStore.Game(ships)!.Events.Any(e => e.Payload.Contains(aStore.Game(ships)!.Fleet!.Salts[0])), "Battleship locks both fleets while keeping unshot cell salts private");
        var invalidProof = SignedGameCommand.Sign(bId, new GameCommand { GameId = ships, Revision = aStore.Game(ships)!.State.Revision, Previous = aStore.Game(ships)!.State.Head, Type = "proof", Cell = 0, Ship = 1, Salt = new string('a', 48) });
        rejected = false; try { GameRules.Apply(aStore.Game(ships)!.State, invalidProof); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Battleship rejects unsolicited or fabricated hit results");
        var pendingShot = Codec.Decode<GameState>(Codec.Encode(aStore.Game(ships)!.State)); pendingShot.PendingCell = 0; pendingShot.PendingDefender = bId.Id;
        rejected = false; try { GameRules.Apply(pendingShot, invalidProof); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Even the expected defender cannot forge a shot result that differs from its cell commitment");
        var targets = Enumerable.Range(0, 100).Where(i => bBoard[i] > 0).ToArray(); var misses = Enumerable.Range(0, 100).Where(i => aBoard[i] == 0).ToArray();
        for (int i = 0; i < targets.Length; i++)
        {
            await a.GameActionAsync(ships, "shot", targets[i]);
            await Until(() => a.CurrentGame(bId.Id)!.State.PendingCell < 0 && (a.CurrentGame(bId.Id)!.State.Turn == bId.Id || a.CurrentGame(bId.Id)!.State.Status != "playing")); await Ready(ships);
            if (i < targets.Length - 1) { await b.GameActionAsync(ships, "shot", misses[i]); await Until(() => a.CurrentGame(bId.Id)!.State.PendingCell < 0 && a.CurrentGame(bId.Id)!.State.Turn == aId.Id); await Ready(ships); }
        }
        await Until(() => a.CurrentGame(bId.Id)!.State.Finished && b.CurrentGame(aId.Id)!.State.Finished); await Ready(ships);
        check(a.CurrentGame(bId.Id)!.State.Winner == aId.Id && a.CurrentGame(bId.Id)!.State.Revealed.Count == 2, "Battleship completes a real network match and verifies both original fleets before confirming victory");
        check(bStore.Game(ships)!.Events.Count == aStore.Game(ships)!.Events.Count, "Both peers retain the identical durable game event history");
        await b.DeleteMessagesAsync(aId.Id, new HashSet<string> { ships });
        check(bStore.Game(ships) is null && bStore.WasDeleted(ships), "Deleting a game card removes its secret fleet and retains a replay tombstone");
        using var reopened = new LocalStore(Path.Combine(root, "a")); check(reopened.Game(ships)!.State.Winner == aId.Id, "Completed games survive reopening the workspace database");
        await a.DeleteMessagesAsync(bId.Id, new HashSet<string> { ships });
        var offline = await a.InviteGameAsync(bId.Id, "caro"); await Ready(offline); await b.GameActionAsync(offline, "accept"); await Ready(offline);
        await a.GameActionAsync(offline, "move", 100); await Ready(offline);
        await a.DisposeAsync(); await Until(() => !b.IsConnected(aId.Id));
        await b.GameActionAsync(offline, "move", 101);
        check(bStore.Game(offline)!.Pending is not null && bStore.Game(offline)!.State.Board[101] == 0, "Offline guest moves are durable pending requests, not prematurely confirmed moves");
        await using var resumed = new PeerNode(aId, aStore); await resumed.StartAsync(0, false, true); await b.ConnectAsync("127.0.0.1", resumed.Port, aId.Id);
        await Until(() => bStore.Game(offline)!.Pending is null && aStore.Game(offline)!.State.Board[101] != 0 && aStore.Game(offline)!.State.Head == bStore.Game(offline)!.State.Head);
        check(aStore.Game(offline)!.Events.Count == 4, "Reopening the host resumes queued play exactly once with matching histories");
    }
}
