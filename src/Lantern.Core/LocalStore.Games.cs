namespace Lantern.Core;

public sealed partial class LocalStore
{
    private void InitializeGames() { using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TABLE IF NOT EXISTS games(id TEXT PRIMARY KEY, conversation TEXT NOT NULL, data TEXT NOT NULL)"; cmd.ExecuteNonQuery(); }
    public GameRecord? Game(string id)
    {
        lock (gate) { using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT data FROM games WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); return cmd.ExecuteScalar() is string value ? Codec.Decode<GameRecord>(Open(value)) : null; }
    }
    public List<GameRecord> Games(string conversation)
    {
        lock (gate)
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT data FROM games WHERE conversation=$c ORDER BY rowid DESC"; cmd.Parameters.AddWithValue("$c", conversation);
            using var read = cmd.ExecuteReader(); var result = new List<GameRecord>(); while (read.Read()) result.Add(Codec.Decode<GameRecord>(Open(read.GetString(0)))); return result;
        }
    }
    public GameRecord? LatestGame(string conversation)
    {
        lock (gate) { using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT data FROM games WHERE conversation=$c ORDER BY rowid DESC LIMIT 1"; cmd.Parameters.AddWithValue("$c", conversation); return cmd.ExecuteScalar() is string value ? Codec.Decode<GameRecord>(Open(value)) : null; }
    }
    public string? LatestGameId(string conversation)
    {
        lock (gate) { using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id FROM games WHERE conversation=$c ORDER BY rowid DESC LIMIT 1"; cmd.Parameters.AddWithValue("$c", conversation); return cmd.ExecuteScalar() as string; }
    }
    internal void SaveGame(GameRecord game, string conversation)
    {
        lock (gate)
        {
            if (WasDeleted(game.State.Id)) return;
            using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO games(id,conversation,data) VALUES($id,$c,$d) ON CONFLICT(id) DO UPDATE SET data=$d";
            cmd.Parameters.AddWithValue("$id", game.State.Id); cmd.Parameters.AddWithValue("$c", conversation); cmd.Parameters.AddWithValue("$d", Seal(Codec.Encode(game))); cmd.ExecuteNonQuery();
        }
    }
}
