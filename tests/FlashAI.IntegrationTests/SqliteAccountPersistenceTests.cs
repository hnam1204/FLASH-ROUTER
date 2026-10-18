using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Domain;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Accounts.Persistence;
using FlashAI.Modules.Activity.Persistence;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class SqliteAccountPersistenceTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task FreshIntegratedDatabaseHasSchemaAndNoPreviewSeed()
    {
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        var tables = await SqliteAccountDatabaseFixture.ReadTableNamesAsync(context);

        Assert.Contains("AccountProfiles", tables);
        Assert.Contains("AccountTags", tables);
        Assert.Contains("ProviderConnections", tables);
        Assert.Contains("__EFMigrationsHistory", tables);
        Assert.Empty(await context.Set<AccountRecord>().ToListAsync());
    }

    [Fact]
    public async Task CreateReadSearchAndSortPersistAcrossContextRecreation()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();

        var result = await commands.CreateAccountAsync(NewModel(
            "Primary@Example.com",
            "Tài khoản chính",
            "ghi chú tiếng Việt",
            ["Cá nhân", "công việc"]));

        Assert.True(result.IsSuccess, result.Message);

        var detail = await reader.GetAccountDetailAsync(
            (await reader.GetAccountsAsync("tài khoản")).Single().AccountId);
        Assert.NotNull(detail);
        Assert.Equal("Primary@Example.com", detail.Email);
        Assert.Contains("Cá nhân", detail.Tags);

        await using var secondScope = _fixture.Services.CreateAsyncScope();
        var secondReader = secondScope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        var afterRecreation = await secondReader.GetAccountsAsync("CÁ NHÂN", null, "name_asc");
        Assert.Single(afterRecreation);
        Assert.Equal("Tài khoản chính", afterRecreation[0].DisplayName);
    }

    [Fact]
    public async Task DuplicateSameProviderFailsButSameEmailDifferentProviderIsAllowed()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();

        Assert.True((await commands.CreateAccountAsync(NewModel("shared@example.com", "OpenAI"))).IsSuccess);
        var duplicate = await commands.CreateAccountAsync(NewModel(" SHARED@example.com ", "OpenAI 2"));
        Assert.Equal("DUPLICATE", duplicate.ErrorCode);

        var otherProvider = NewModel("shared@example.com", "Google");
        otherProvider.Provider = "google";
        var allowed = await commands.CreateAccountAsync(otherProvider);
        Assert.True(allowed.IsSuccess, allowed.Message);
    }

    [Fact]
    public async Task UpdateUsesVersionAndRejectsStaleWriter()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        Assert.True((await commands.CreateAccountAsync(NewModel("version@example.com", "Versioned"))).IsSuccess);

        var account = (await reader.GetAccountsAsync("version@example.com")).Single();
        var first = await reader.GetAccountDetailAsync(account.AccountId);
        var stale = first!.Version;

        var firstEdit = NewModel("version@example.com", "First update");
        firstEdit.AccountId = account.AccountId;
        firstEdit.Version = stale;
        Assert.True((await commands.UpdateAccountAsync(account.AccountId, firstEdit)).IsSuccess);

        var staleEdit = NewModel("version@example.com", "Stale update");
        staleEdit.AccountId = account.AccountId;
        staleEdit.Version = stale;
        var conflict = await commands.UpdateAccountAsync(account.AccountId, staleEdit);
        Assert.Equal("CONFLICT", conflict.ErrorCode);
    }

    [Fact]
    public async Task FavoriteArchiveRestoreAndDeleteAreDurable()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        Assert.True((await commands.CreateAccountAsync(NewModel("lifecycle@example.com", "Lifecycle"))).IsSuccess);
        var accountId = (await reader.GetAccountsAsync("lifecycle@example.com")).Single().AccountId;

        Assert.True((await commands.ToggleFavoriteAsync(accountId)).IsSuccess);
        Assert.True((await commands.ArchiveAccountAsync(accountId)).IsSuccess);
        Assert.Empty(await reader.GetAccountsAsync());
        Assert.Single(await reader.GetAccountsAsync(null, "archived"));
        Assert.True((await commands.RestoreAccountAsync(accountId)).IsSuccess);
        Assert.Single(await reader.GetAccountsAsync());
        Assert.True((await commands.DeleteAccountAsync(accountId)).IsSuccess);
        Assert.Null(await reader.GetAccountDetailAsync(accountId));
    }

    [Fact]
    public async Task InvalidInputDoesNotWriteAndProviderMetadataHasNoSecretFields()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var invalid = await commands.CreateAccountAsync(NewModel("not-an-email", "Invalid"));
        Assert.Equal("VALIDATION_FAILED", invalid.ErrorCode);

        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        Assert.Empty(await reader.GetAccountsAsync());

        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        context.Add(new ProviderConnectionRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "openai",
            ConnectionKind.ApiKey,
            "OpenAI API metadata",
            null,
            ConnectionStatus.NotConfigured,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        var columns = await SqliteAccountDatabaseFixture.ReadColumnsAsync(context, "ProviderConnections");
        Assert.DoesNotContain(columns, column => column.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ForeignKeysAreEnforcedForAccountTags()
    {
        await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
        context.Add(new AccountTagRecord(Guid.NewGuid(), "orphan", "ORPHAN"));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static AccountEditorVm NewModel(
        string email,
        string displayName,
        string? notes = null,
        IReadOnlyList<string>? tags = null)
        => new()
        {
            Email = email,
            DisplayName = displayName,
            Notes = notes,
            Provider = "openai",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified",
            Tags = tags?.ToList() ?? []
        };
}

public sealed class SqliteAccountDatabaseFixture : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Directory.GetCurrentDirectory(),
        ".local",
        "test-data",
        $"be01-{Guid.NewGuid():N}");

    public ServiceProvider Services { get; private set; } = null!;

    public string Root => _root;

    public IDbContextFactory<FlashAiDbContext> ContextFactory
        => Services.GetRequiredService<IDbContextFactory<FlashAiDbContext>>();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        var paths = new LocalDataPaths(_root);
        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IFlashAiModelContributor, AccountModelContributor>();
        services.AddSingleton<IFlashAiModelContributor, ConnectionModelContributor>();
        services.AddSingleton<IFlashAiModelContributor, ActivityModelContributor>();
        services.AddDbContextFactory<FlashAiDbContext>((serviceProvider, options) =>
        {
            options.UseSqlite(
                serviceProvider.GetRequiredService<SqliteConnectionFactory>().CreateConnectionString(),
                sqlite => sqlite.MigrationsAssembly("FLASH_ROUTER"));
        });
        services.AddSingleton<MigrationCoordinator>();
        services.AddScoped<SqliteAccountWorkspaceReader>();
        services.AddScoped<SqliteAccountCommandPort>();
        Services = services.BuildServiceProvider();

        await Services.GetRequiredService<SqliteConnectionFactory>().PrepareDatabaseAsync();
        await Services.GetRequiredService<MigrationCoordinator>().MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    public static async Task<IReadOnlyList<string>> ReadTableNamesAsync(FlashAiDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            var values = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public static async Task<IReadOnlyList<string>> ReadColumnsAsync(FlashAiDbContext context, string tableName)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
            var values = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(1));
            }

            return values;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}

