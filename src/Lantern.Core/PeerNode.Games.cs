namespace Lantern.Core;

public sealed partial class PeerNode
{
    public event Action? GamesChanged;
    private readonly SemaphoreSlim gameChanges = new(1, 1);
    public GameView? CurrentGame(string conversation, string? gameId = null)
    {
        if (FindPeer(conversation) is not { Trusted: true, Blocked: false } peer) return null;
        var record = gameId is null ? Store.LatestGame(conversation) : Store.Game(gameId);
        if (record is null || record.State.Other(Identity.Id) != conversation || Store.WasDeleted(record.State.Id)) return null;
        return new(record.State, Identity.Id, peer.DisplayName, peer.Connected, record.Pending is not null || record.State.Host == Identity.Id && record.Acknowledged < record.Events.Count, record.Fleet?.Board ?? [], record.Error);
    }
    public async Task<string> InviteGameAsync(string conversation, string kind, string? starter = null)
    {
        var peer = FindPeer(conversation);
        if (peer is not { Trusted: true, Blocked: false, Connected: true }) throw new InvalidOperationException("Mời chơi trong chat riêng với người đã ghép đôi và đang online.");
        if (peer.ProtocolVersion < 6) throw new InvalidOperationException("Đối thủ cần cập nhật Lantern để chơi game.");
        SignedGameCommand signed; GameRecord record;
        await gameChanges.WaitAsync(lifetime.Token);
        try
        {
            if (Store.Games(conversation).Any(g => !g.State.Finished)) throw new InvalidOperationException("Hội thoại đã có lời mời hoặc ván đang chơi.");
            var command = new GameCommand { GameId = Guid.NewGuid().ToString("N"), Type = "invite", Kind = kind, Opponent = conversation, Starter = starter ?? Identity.Id, Text = WordChain.Version };
            signed = SignedGameCommand.Sign(Identity, command); record = new() { State = GameRules.Apply(new(), signed), Events = [signed] }; PersistGame(record);
        }
        finally { gameChanges.Release(); }
        GamesChanged?.Invoke(); await SendGameEvent(conversation, signed); return record.State.Id;
    }
    public async Task GameActionAsync(string id, string type, int cell = -1, string text = "", int[]? board = null, int? expectedRevision = null)
    {
        SignedGameCommand signed; string peer; bool host;
        await gameChanges.WaitAsync(lifetime.Token);
        try
        {
            var record = Store.Game(id) ?? throw new InvalidOperationException("Ván không còn trên máy này."); var state = record.State;
            peer = state.Other(Identity.Id); host = state.Host == Identity.Id;
            if (FindPeer(peer) is not { Trusted: true, Blocked: false } || Store.WasDeleted(id)) throw new InvalidOperationException("Hội thoại không còn khả dụng.");
            if (record.Pending is not null || host && record.Acknowledged < record.Events.Count) throw new InvalidOperationException("Đang chờ đối thủ xác nhận lượt trước.");
            if (expectedRevision is { } expected && state.Revision != expected) throw new InvalidOperationException("Ván đã đổi lượt. Đợi đồng bộ rồi thử lại.");
            var command = new GameCommand { GameId = id, Revision = state.Revision, Previous = state.Head, Type = type, Cell = cell, Text = text };
            if (type == "fleet")
            {
                if (record.Fleet is not null && state.Fleets.ContainsKey(Identity.Id)) throw new InvalidOperationException("Đội tàu đã khóa.");
                record.Fleet = GameFleet.Create(board ?? GameFleet.RandomBoard()); command.Commitments = record.Fleet.Commitments(id, Identity.Id);
            }
            if (type == "proof")
            {
                if (record.Fleet is null || state.PendingDefender != Identity.Id || state.PendingCell < 0) throw new InvalidOperationException("Không có kết quả bắn cần xác nhận.");
                command.Cell = state.PendingCell; command.Ship = record.Fleet.Board[command.Cell]; command.Salt = record.Fleet.Salts[command.Cell];
            }
            if (type == "reveal")
            {
                if (record.Fleet is null) throw new InvalidOperationException("Thiếu đội tàu đã khóa.");
                command.Board = record.Fleet.Board; command.Salts = record.Fleet.Salts;
            }
            signed = SignedGameCommand.Sign(Identity, command);
            var next = GameRules.Apply(state, signed); // Validate before persisting even a queued request.
            record.Error = "";
            if (host) { record.State = next; record.Events.Add(signed); } else record.Pending = signed;
            PersistGame(record);
        }
        finally { gameChanges.Release(); }
        GamesChanged?.Invoke();
        if (host) await SendGameEvent(peer, signed); else await SendTo(peer, new WireMessage { Type = "game-request", Id = id, Data = Codec.Encode(signed) });
    }
    private void PersistGame(GameRecord record)
    {
        var state = record.State; var peer = state.Other(Identity.Id); Store.SaveGame(record, peer);
        if (Store.Message(state.Id) is null && !Store.WasDeleted(state.Id))
        {
            var message = new ChatMessage { Id = state.Id, GameId = state.Id, ConversationId = peer, SenderId = state.Host, SenderName = state.Host == Identity.Id ? Identity.Name : FindPeer(peer)?.Name ?? "", Outgoing = state.Host == Identity.Id, Kind = "text", Text = "🎮 " + GameState.Title(state.Kind) + " · Lời mời chơi", Status = "delivered" };
            if (message.Outgoing) Store.SaveMessage(message); else { Store.SaveIncomingMessage(message); MessageReceived?.Invoke(message); }
        }
    }
    private Task SendGameEvent(string peer, SignedGameCommand command) => SendTo(peer, new WireMessage { Type = "game-event", Id = command.Verify().GameId, Data = Codec.Encode(command) });
    private async Task ReplayGames(string peer)
    {
        if (FindPeer(peer)?.ProtocolVersion < 6) return;
        foreach (var game in Store.Games(peer).Where(g => !Store.WasDeleted(g.State.Id)).Take(30))
        {
            if (game.State.Host == Identity.Id)
            {
                // The peer answers with its durable event count; never assume its disk
                // still contains an acknowledgement from a previous installation.
                await SendTo(peer, new WireMessage { Type = "game-head", Id = game.State.Id, Offset = game.Events.Count });
            }
            else
            {
                await SendTo(peer, new WireMessage { Type = "game-sync", Id = game.State.Id, Offset = game.Events.Count });
                if (game.Pending is { } pending) await SendTo(peer, new WireMessage { Type = "game-request", Id = game.State.Id, Data = Codec.Encode(pending) });
            }
        }
    }
    private async Task DriveGame(string id)
    {
        var record = Store.Game(id);
        if (record is null || record.Pending is not null || record.Fleet is null || record.State.Finished || record.State.Host == Identity.Id && record.Acknowledged < record.Events.Count) return;
        try
        {
            if (record.State.PendingDefender == Identity.Id) await GameActionAsync(id, "proof");
            else if (record.State.Status == "verifying" && !record.State.Revealed.Contains(Identity.Id)) await GameActionAsync(id, "reveal");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { }
    }
    private async Task HandleGame(string peer, WireMessage wire)
    {
        if (!Protocol.ValidId(wire.Id) || wire.Data.Length > 40000 || wire.Offset is < 0 or > 2048) throw new InvalidDataException("Gói game không hợp lệ.");
        if (Store.WasDeleted(wire.Id)) { if (wire.Type != "game-gone") await SendTo(peer, new WireMessage { Type = "game-gone", Id = wire.Id }); return; }
        var outgoing = new List<WireMessage>(); bool changed = false;
        await gameChanges.WaitAsync(lifetime.Token);
        try
        {
            var record = Store.Game(wire.Id);
            if (record is not null && record.State.Other(Identity.Id) != peer) return;
            if (wire.Type == "game-head") { outgoing.Add(new() { Type = "game-sync", Id = wire.Id, Offset = record?.Events.Count ?? 0 }); }
            else if (wire.Type == "game-sync" && record?.State.Host == Identity.Id)
            {
                foreach (var item in record.Events.Skip((int)wire.Offset)) outgoing.Add(new() { Type = "game-event", Id = wire.Id, Data = Codec.Encode(item) });
                if (wire.Offset == record.Events.Count && record.Acknowledged < record.Events.Count) { record.Acknowledged = record.Events.Count; PersistGame(record); changed = true; }
            }
            else if (wire.Type == "game-ack" && record?.State.Host == Identity.Id && wire.Offset <= record.Events.Count)
            {
                if (wire.Offset > record.Acknowledged) { record.Acknowledged = (int)wire.Offset; PersistGame(record); changed = true; }
            }
            else if (wire.Type == "game-reject" && record is not null)
            {
                if (record.State.Host == peer && record.Pending?.Verify().Id == wire.Name) { record.Pending = null; record.Error = wire.Data; PersistGame(record); changed = true; }
                else if (record.State.Host == Identity.Id && record.State.Status == "invited")
                {
                    var cancel = SignedGameCommand.Sign(Identity, new() { GameId = wire.Id, Revision = record.State.Revision, Previous = record.State.Head, Type = "cancel" });
                    record.State = GameRules.Apply(record.State, cancel); record.Events.Add(cancel); record.Acknowledged = record.Events.Count; record.Error = wire.Data; PersistGame(record); changed = true;
                }
            }
            else if (wire.Type == "game-gone" && record is not null)
            {
                // Deleting history pauses the match; it never awards a win.
                record.Error = "Đối thủ đã xóa lịch sử ván. Đóng ván này để chơi ván mới."; record.State.Status = "cancelled"; record.Pending = null; record.Acknowledged = record.Events.Count; PersistGame(record); changed = true;
            }
            else if (wire.Type is "game-request" or "game-event")
            {
                var signed = Codec.Decode<SignedGameCommand>(wire.Data); var command = signed.Verify();
                if (command.GameId != wire.Id || command.Actor != peer && command.Actor != Identity.Id) throw new InvalidDataException("Sai người chơi.");
                if (record is null)
                {
                    if (wire.Type != "game-event" || command.Type != "invite" || command.Actor != peer || command.Opponent != Identity.Id) return;
                    if (Store.Games(peer).Any(g => !g.State.Finished)) { outgoing.Add(new() { Type = "game-reject", Id = wire.Id, Data = "Hội thoại đang có lời mời hoặc ván khác. Hãy thử lại." }); }
                    else { record = new() { State = GameRules.Apply(new(), signed), Events = [signed] }; PersistGame(record); changed = true; outgoing.Add(new() { Type = "game-ack", Id = wire.Id, Offset = 1 }); }
                }
                else if (wire.Type == "game-request" && record.State.Host == Identity.Id && command.Actor == peer)
                {
                    if (record.Events.Any(item => item.Hash == signed.Hash)) outgoing.Add(new() { Type = "game-event", Id = wire.Id, Data = Codec.Encode(signed) });
                    else
                    {
                        try { record.State = GameRules.Apply(record.State, signed); record.Events.Add(signed); PersistGame(record); changed = true; outgoing.Add(new() { Type = "game-event", Id = wire.Id, Data = Codec.Encode(signed) }); }
                        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { outgoing.Add(new() { Type = "game-reject", Id = wire.Id, Name = command.Id, Data = ex.Message }); }
                    }
                }
                else if (wire.Type == "game-event" && record.State.Host == peer)
                {
                    if (command.Revision == record.Events.Count)
                    {
                        record.State = GameRules.Apply(record.State, signed); record.Events.Add(signed);
                        if (record.Pending?.Hash == signed.Hash) record.Pending = null;
                        PersistGame(record); changed = true;
                    }
                    else if (command.Revision < record.Events.Count && record.Events[command.Revision].Hash != signed.Hash) throw new InvalidDataException("Lịch sử ván không khớp.");
                    if (command.Revision > record.Events.Count) outgoing.Add(new() { Type = "game-sync", Id = wire.Id, Offset = record.Events.Count });
                    outgoing.Add(new() { Type = "game-ack", Id = wire.Id, Offset = record.Events.Count });
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { throw new InvalidDataException("Sự kiện game không hợp lệ.", ex); }
        finally { gameChanges.Release(); }
        if (changed) GamesChanged?.Invoke();
        foreach (var response in outgoing) await SendTo(peer, response);
        await DriveGame(wire.Id);
    }
}
