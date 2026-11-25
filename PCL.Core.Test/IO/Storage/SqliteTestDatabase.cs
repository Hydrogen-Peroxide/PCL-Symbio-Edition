using Microsoft.Data.Sqlite;

namespace PCL.Core.Test.IO.Storage;

internal static class SqliteTestDatabase
{
    internal static void ClearPools(string path)
    {
        // The schema and the cache storage use different connection strings for the same database.
        using var defaultPool = new SqliteConnection($"Data Source={path}");
        using var explicitPool = new SqliteConnection($"Data Source={path};Pooling=True");
        SqliteConnection.ClearPool(defaultPool);
        SqliteConnection.ClearPool(explicitPool);
    }
}
