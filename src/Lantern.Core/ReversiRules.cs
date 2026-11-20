namespace Lantern.Core;

public static class ReversiRules
{
    public static int[] Opening()
    {
        var board = new int[64]; board[27] = board[36] = 2; board[28] = board[35] = 1; return board;
    }
    public static int[] Flips(IReadOnlyList<int> board, int cell, int mark)
    {
        if (board.Count != 64 || cell is < 0 or >= 64 || board[cell] != 0 || mark is not (1 or 2)) return [];
        var result = new List<int>();
        foreach (var (dx, dy) in new[] { (-1,-1), (0,-1), (1,-1), (-1,0), (1,0), (-1,1), (0,1), (1,1) })
        {
            var line = new List<int>(); int x = cell % 8 + dx, y = cell / 8 + dy;
            while (x is >= 0 and < 8 && y is >= 0 and < 8 && board[y * 8 + x] == 3 - mark) { line.Add(y * 8 + x); x += dx; y += dy; }
            if (line.Count > 0 && x is >= 0 and < 8 && y is >= 0 and < 8 && board[y * 8 + x] == mark) result.AddRange(line);
        }
        return result.ToArray();
    }
    public static int[] LegalMoves(IReadOnlyList<int> board, int mark) => Enumerable.Range(0, 64).Where(i => Flips(board, i, mark).Length > 0).ToArray();
}
