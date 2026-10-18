using Microsoft.Data.Sqlite;

namespace FlashAI.IntegrationTests;

public sealed class CanonicalDatabaseIntegrityTests
{
    [Fact]
    public async Task CanonicalRuntimeDatabasePassesReadOnlyIntegrityChecks()
    {
        var root = FindWorkspaceRoot();
        var path = Path.Combine(root, ".local", "data", "flashrouter.db");
        Assert.True(File.Exists(path), $"Canonical runtime database was not found: {path}");

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        Assert.Equal("ok", await ExecuteScalarAsync(connection, "PRAGMA integrity_check;"));
        Assert.Empty(await ReadRowsAsync(connection, "PRAGMA foreign_key_check;"));
    }

    private static async Task<string?> ExecuteScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static async Task<List<string>> ReadRowsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
        }

        return rows;
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Workspace root not found.");
    }
}
