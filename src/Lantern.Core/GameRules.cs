using System.Text;

namespace Lantern.Core;

public static class GameRules
{
    public static GameState Apply(GameState current, SignedGameCommand signed)
    {
        var c = signed.Verify(); var s = Codec.Decode<GameState>(Codec.Encode(current));
        if (c.Revision != s.Revision || c.Previous != s.Head || s.Revision >= 2048) throw new InvalidOperationException("Ván đã đổi lượt. Đợi đồng bộ rồi thử lại.");
        if (s.Revision == 0)
        {
            if (c.Type != "invite" || c.Kind is not ("caro" or "ships" or "words" or "reversi") || c.Opponent.Length != 64 || !c.Opponent.All(Uri.IsHexDigit) || c.Opponent == c.Actor || c.Starter != c.Actor && c.Starter != c.Opponent || c.Text != WordChain.Version) throw new InvalidOperationException("Lời mời game không hợp lệ hoặc khác phiên bản luật.");
            s.Id = c.GameId; s.Host = c.Actor; s.Guest = c.Opponent; s.Kind = c.Kind; s.Starter = s.Turn = c.Starter;
            if (s.Kind == "reversi") s.Board = ReversiRules.Opening();
        }
        else
        {
            if (c.GameId != s.Id || c.Actor != s.Host && c.Actor != s.Guest || s.Finished) throw new InvalidOperationException("Ván đã kết thúc hoặc bạn không tham gia.");
            if (s.Status == "invited")
            {
                if (c.Type == "accept" && c.Actor == s.Guest) s.Status = s.Kind == "ships" ? "placing" : "playing";
                else if (c.Type == "decline" && c.Actor == s.Guest) s.Status = "declined";
                else if (c.Type == "cancel" && c.Actor == s.Host) s.Status = "cancelled";
                else throw new InvalidOperationException("Lời mời chưa được chấp nhận.");
            }
            else if (c.Type == "resign") Finish(s, s.Other(c.Actor), "Đối thủ đầu hàng");
            else if (c.Type == "draw") { if (s.DrawOffer.Length > 0 && s.DrawOffer != c.Actor) Finish(s, "", "Hai bên đồng ý hòa"); else s.DrawOffer = c.Actor; }
            else if (c.Type == "draw-decline" && s.DrawOffer.Length > 0 && s.DrawOffer != c.Actor) s.DrawOffer = "";
            else if (s.Kind == "ships") Ships(s, c);
            else if (s.Kind == "words") Words(s, c);
            else if (s.Kind == "reversi") Reversi(s, c);
            else Caro(s, c);
        }
        if (s.Revision >= 2047 && !s.Finished) Finish(s, "", "Ván đạt giới hạn lượt lưu trữ");
        s.Revision++; s.Head = signed.Hash; return s;
    }
    private static void NeedTurn(GameState s, GameCommand c)
    {
        if (s.Status != "playing" || s.Turn != c.Actor) throw new InvalidOperationException("Chưa đến lượt bạn.");
    }
    private static void Finish(GameState s, string winner, string reason) { s.Status = "finished"; s.Winner = winner; s.Reason = reason; s.DrawOffer = ""; }
    private static void Reversi(GameState s, GameCommand c)
    {
        NeedTurn(s, c);
        int mark = c.Actor == s.Starter ? 1 : 2;
        var flips = ReversiRules.Flips(s.Board, c.Cell, mark);
        if (c.Type != "move" || flips.Length == 0) throw new InvalidOperationException("Chọn ô gợi ý để kẹp và lật ít nhất một quân đối thủ.");
        s.Board[c.Cell] = mark; foreach (int cell in flips) s.Board[cell] = mark; s.LastCell = c.Cell; s.WinningCells = flips.ToList();
        if (ReversiRules.LegalMoves(s.Board, 3 - mark).Length > 0) { s.Turn = s.Other(c.Actor); s.Reason = ""; }
        else if (ReversiRules.LegalMoves(s.Board, mark).Length > 0) { s.Turn = c.Actor; s.Reason = "Đối thủ không có nước hợp lệ · Bạn đi tiếp"; }
        else
        {
            int black = s.Board.Count(v => v == 1), white = s.Board.Count(v => v == 2);
            Finish(s, black == white ? "" : black > white ? s.Starter : s.Other(s.Starter), $"Hết nước đi · Đen {black} — Trắng {white}");
        }
    }
    private static void Caro(GameState s, GameCommand c)
    {
        NeedTurn(s, c);
        if (c.Type != "move" || c.Cell is < 0 or >= 225 || s.Board[c.Cell] != 0) throw new InvalidOperationException("Chọn một ô còn trống.");
        int mark = c.Actor == s.Starter ? 1 : 2; s.Board[c.Cell] = mark; s.LastCell = c.Cell;
        foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
        {
            var line = new List<int> { c.Cell };
            foreach (int direction in new[] { -1, 1 })
                for (int x = c.Cell % 15 + dx * direction, y = c.Cell / 15 + dy * direction; x is >= 0 and < 15 && y is >= 0 and < 15 && s.Board[y * 15 + x] == mark; x += dx * direction, y += dy * direction) line.Add(y * 15 + x);
            if (line.Count >= 5) { s.WinningCells = line; Finish(s, c.Actor, "Nối đủ 5 quân"); return; }
        }
        if (s.Board.All(v => v != 0)) Finish(s, "", "Bàn cờ đã đầy"); else s.Turn = s.Other(c.Actor);
    }
    private static void Words(GameState s, GameCommand c)
    {
        if (s.Status != "playing") throw new InvalidOperationException("Ván chưa bắt đầu.");
        if (c.Type is "word-approve" or "word-reject")
        {
            if (s.ProposedWord.Length == 0 || c.Actor == s.Turn) throw new InvalidOperationException("Không có từ cần bạn công nhận.");
            if (c.Type == "word-approve") { s.Words.Add(s.ProposedWord); s.Turn = c.Actor; if (s.Words.Count >= 500) Finish(s, "", "Hoàn thành chuỗi 500 từ"); }
            s.ProposedWord = ""; return;
        }
        NeedTurn(s, c);
        if (s.ProposedWord.Length > 0 || c.Type is not ("word" or "word-propose")) throw new InvalidOperationException("Đang chờ đối thủ công nhận từ.");
        var word = WordChain.Normalize(c.Text); var parts = word.Split(' ');
        if (parts.Length != 2 || parts.Any(p => p.Length is < 1 or > 16 || !p.All(char.IsLetter))) throw new ArgumentException("Nhập đúng hai tiếng tiếng Việt.");
        if (s.Words.Contains(word)) throw new InvalidOperationException("Từ này đã được dùng trong ván.");
        if (s.Words.Count > 0 && parts[0] != s.Words[^1].Split(' ')[1]) throw new InvalidOperationException("Cần bắt đầu bằng “" + s.Words[^1].Split(' ')[1] + "”.");
        if (c.Type == "word-propose") { s.ProposedWord = word; return; }
        if (!WordChain.Contains(word)) throw new InvalidOperationException("Từ chưa có trong từ điển. Có thể đề nghị đối thủ công nhận.");
        s.Words.Add(word); s.Turn = s.Other(c.Actor);
        if (s.Words.Count >= 500) Finish(s, "", "Hoàn thành chuỗi 500 từ");
    }
    private static void Ships(GameState s, GameCommand c)
    {
        if (c.Type == "fleet")
        {
            if (s.Status != "placing" || s.Fleets.ContainsKey(c.Actor) || c.Commitments.Length != 100 || c.Commitments.Any(v => v is null || v.Length != 64 || !v.All(Uri.IsHexDigit))) throw new InvalidOperationException("Đội tàu đã chốt hoặc chưa hợp lệ.");
            s.Fleets[c.Actor] = c.Commitments; s.Shots[c.Actor] = [];
            if (s.Fleets.Count == 2) s.Status = "playing"; return;
        }
        if (c.Type == "shot")
        {
            NeedTurn(s, c); var target = s.Other(c.Actor);
            if (s.PendingCell >= 0 || c.Cell is < 0 or >= 100 || s.Shots[target].ContainsKey(c.Cell)) throw new InvalidOperationException("Ô này đã bắn hoặc đang chờ kết quả.");
            s.PendingCell = c.Cell; s.PendingDefender = target; return;
        }
        if (c.Type == "proof")
        {
            if (s.Status != "playing" || s.PendingCell < 0 || c.Actor != s.PendingDefender || c.Cell != s.PendingCell || c.Ship is < 0 or > 5 || c.Salt.Length != 48 || !c.Salt.All(Uri.IsHexDigit) || GameFleet.Commitment(s.Id, c.Actor, c.Cell, c.Ship, c.Salt) != s.Fleets[c.Actor][c.Cell]) throw new InvalidOperationException("Kết quả bắn không khớp đội tàu đã khóa.");
            var shots = s.Shots[c.Actor]; shots.Add(c.Cell, c.Ship); s.LastCell = c.Cell; s.PendingCell = -1; s.PendingDefender = "";
            if (c.Ship > 0 && shots.Values.Count(v => v == c.Ship) > GameFleet.Lengths[c.Ship - 1]) { Finish(s, s.Other(c.Actor), "Đội tàu đối thủ không hợp lệ"); return; }
            if (shots.Values.Count(v => v > 0) == 17 || shots.Count == 100) { s.Status = "verifying"; s.Winner = s.Other(c.Actor); }
            else s.Turn = c.Actor;
            return;
        }
        if (c.Type == "reveal")
        {
            if (s.Status != "verifying" || s.Revealed.Contains(c.Actor) || c.Board.Length != 100 || c.Salts.Length != 100) throw new InvalidOperationException("Chưa đến bước kiểm chứng đội tàu.");
            for (int i = 0; i < 100; i++) if (c.Salts[i] is null || c.Salts[i].Length != 48 || GameFleet.Commitment(s.Id, c.Actor, i, c.Board[i], c.Salts[i]) != s.Fleets[c.Actor][i]) throw new InvalidOperationException("Đội tàu đã bị thay đổi.");
            try { GameFleet.Validate(c.Board); } catch (ArgumentException) { Finish(s, s.Other(c.Actor), "Đội tàu đối thủ không hợp lệ"); return; }
            s.Revealed.Add(c.Actor);
            if (s.Revealed.Count == 2) Finish(s, s.Winner, "Đánh chìm toàn bộ đội tàu · Đã kiểm chứng");
            return;
        }
        throw new InvalidOperationException("Thao tác Bắn tàu không hợp lệ.");
    }
}

public static class WordChain
{
    public const string Version = "lantern-games-1";
    public static string Normalize(string text) => string.Join(" ", text.Normalize(NormalizationForm.FormC).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static readonly HashSet<string> Dictionary = Load();
    private static HashSet<string> Load()
    {
        using var input = typeof(WordChain).Assembly.GetManifestResourceStream("Lantern.Core.word-chain.vi.txt")!;
        using var reader = new StreamReader(input); return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(Normalize).Where(w => w.Length > 0).ToHashSet();
    }
    public static bool Contains(string word) => Dictionary.Contains(Normalize(word));
}
