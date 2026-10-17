using Microsoft.Data.Sqlite;

namespace FlashAI.Foundation.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly LocalDataPaths _paths;

    public SqliteConnectionFactory(LocalDataPaths paths)
    {
        _paths = paths;
    }

    public string CreateConnectionString()
    {
        Directory.CreateDirectory(_paths.Data);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            DefaultTimeout = 5
        };

        return builder.ToString();
    }

    public async Task PrepareDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(CreateConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using (var journalCommand = connection.CreateCommand())
        {
            journalCommand.CommandText = "PRAGMA journal_mode=WAL;";
            await journalCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var busyTimeoutCommand = connection.CreateCommand())
        {
            busyTimeoutCommand.CommandText = "PRAGMA busy_timeout=5000;";
            await busyTimeoutCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var foreignKeysCommand = connection.CreateCommand())
        {
            foreignKeysCommand.CommandText = "PRAGMA foreign_keys=ON;";
            await foreignKeysCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
