using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Lantern.Core;

public sealed class GameCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GameId { get; set; } = "";
    public string Actor { get; set; } = "";
    public int Revision { get; set; }
    public string Previous { get; set; } = "";
    public string Type { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Opponent { get; set; } = "";
    public string Starter { get; set; } = "";
    public int Cell { get; set; } = -1;
    public string Text { get; set; } = "";
    public int Ship { get; set; }
    public string Salt { get; set; } = "";
    public string[] Commitments { get; set; } = [];
    public int[] Board { get; set; } = [];
    public string[] Salts { get; set; } = [];
}
public sealed record SignedGameCommand(string Payload, string PublicKey, string Signature)
{
    public string Hash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Payload)));
    public static SignedGameCommand Sign(Identity identity, GameCommand command)
    {
        command.Actor = identity.Id; var payload = Codec.Encode(command);
        using var key = identity.Certificate.GetECDsaPrivateKey()!;
        return new(payload, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), Convert.ToBase64String(key.SignData(Bytes(payload), HashAlgorithmName.SHA256)));
    }
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes("Lantern-game-v1\n" + text);
    public GameCommand Verify()
    {
        try
        {
            if (Payload is null || Payload.Length > 20000 || PublicKey is null || PublicKey.Length > 1024 || Signature is null || Signature.Length > 256) throw new InvalidDataException("Lượt chơi không hợp lệ.");
            var command = Codec.Decode<GameCommand>(Payload);
            if (command.Id is null || !Protocol.ValidId(command.Id) || command.GameId is null || !Protocol.ValidId(command.GameId) || command.Actor is null || command.Previous is null || command.Text is null || command.Type is null || command.Opponent is null || command.Starter is null || command.Kind is null || command.Salt is null || command.Commitments is null || command.Board is null || command.Salts is null || command.Text.Length > 80 || command.Revision is < 0 or > 2048) throw new InvalidDataException("Lượt chơi không hợp lệ.");
            var bytes = Convert.FromBase64String(PublicKey);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != command.Actor) throw new InvalidDataException("Sai danh tính người chơi.");
            using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(bytes, out var used);
            if (used != bytes.Length || key.KeySize != 256 || !key.VerifyData(Bytes(Payload), Convert.FromBase64String(Signature), HashAlgorithmName.SHA256)) throw new InvalidDataException("Chữ ký lượt chơi không hợp lệ.");
            return command;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or System.Text.Json.JsonException or ArgumentException) { throw new InvalidDataException("Không xác minh được lượt chơi.", ex); }
    }
}
public sealed class GameState
{
    public string Id { get; set; } = "";
    public string Host { get; set; } = "";
    public string Guest { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Starter { get; set; } = "";
    public string Turn { get; set; } = "";
    public string Status { get; set; } = "invited";
    public string Winner { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Head { get; set; } = "";
    public int Revision { get; set; }
    public int[] Board { get; set; } = new int[225];
    public int LastCell { get; set; } = -1;
    public List<int> WinningCells { get; set; } = [];
    public List<string> Words { get; set; } = [];
    public string ProposedWord { get; set; } = "";
    public string DrawOffer { get; set; } = "";
    public Dictionary<string, string[]> Fleets { get; set; } = [];
    // Maps defender -> cell -> revealed ship number; -1 is never used as a result.
    public Dictionary<string, Dictionary<int, int>> Shots { get; set; } = [];
    public List<string> Revealed { get; set; } = [];
    public string PendingDefender { get; set; } = "";
    public int PendingCell { get; set; } = -1;
    public bool Finished => Status is "finished" or "declined" or "cancelled";
    public string Other(string actor) => actor == Host ? Guest : Host;
    public static string Title(string kind) => kind switch { "caro" => "Caro", "ships" => "Bắn tàu", "words" => "Nối chữ", "reversi" => "Cờ lật", _ => "Game" };
}
public sealed class GameFleet
{
    public int[] Board { get; set; } = new int[100];
    public string[] Salts { get; set; } = [];
    public static readonly int[] Lengths = [5, 4, 3, 3, 2];
    public static void Validate(int[] board)
    {
        if (board.Length != 100 || board.Any(v => v is < 0 or > 5)) throw new ArgumentException("Bàn tàu cần 100 ô.");
        for (int ship = 1; ship <= 5; ship++)
        {
            var cells = Enumerable.Range(0, 100).Where(i => board[i] == ship).ToArray();
            if (cells.Length != Lengths[ship - 1] || !(cells.All(i => i / 10 == cells[0] / 10) && cells.Last() - cells[0] == cells.Length - 1 || cells.All(i => i % 10 == cells[0] % 10) && cells.Last() - cells[0] == (cells.Length - 1) * 10)) throw new ArgumentException("Mỗi tàu phải nằm thẳng, liền nhau; độ dài 5–4–3–3–2.");
        }
    }
    public static GameFleet Create(int[] board)
    {
        Validate(board); return new() { Board = board.ToArray(), Salts = Enumerable.Range(0, 100).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(24))).ToArray() };
    }
    public static int[] RandomBoard()
    {
        var board = new int[100];
        for (int ship = 1; ship <= 5; ship++)
        {
            while (true)
            {
                var cell = RandomNumberGenerator.GetInt32(100); var vertical = RandomNumberGenerator.GetInt32(2) == 1; var length = Lengths[ship - 1];
                if ((vertical ? cell / 10 : cell % 10) + length > 10) continue;
                var cells = Enumerable.Range(0, length).Select(i => cell + i * (vertical ? 10 : 1)).ToArray();
                if (cells.Any(i => board[i] != 0)) continue;
                foreach (var i in cells) board[i] = ship; break;
            }
        }
        return board;
    }
    public static string Commitment(string game, string actor, int cell, int ship, string salt) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"Lantern-fleet-v1|{game}|{actor}|{cell}|{ship}|{salt}")));
    public string[] Commitments(string game, string actor) => Board.Select((ship, cell) => Commitment(game, actor, cell, ship, Salts[cell])).ToArray();
}
public sealed class GameRecord
{
    public GameState State { get; set; } = new();
    public List<SignedGameCommand> Events { get; set; } = [];
    public SignedGameCommand? Pending { get; set; }
    public GameFleet? Fleet { get; set; }
    public int Acknowledged { get; set; }
    public string Error { get; set; } = "";
}
public sealed record GameView(GameState State, string Me, string PeerName, bool Connected, bool Pending, int[] OwnBoard, string Error);
