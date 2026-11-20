using Microsoft.Data.Sqlite;

namespace Lantern.Core;

public sealed partial class LocalStore
{
    internal void Snapshot(string destination)
    {
        lock (gate)
        {
            using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
            target.Open(); db.BackupDatabase(target);
        }
    }
}
