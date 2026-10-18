using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Domain;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Persistence;

namespace FlashAI.IntegrationTests;

public sealed class ProviderConnectionCatalogSqliteTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();

    [Fact]
    public async Task EligibleConnectionsOrderDateTimeOffsetInMemoryWithoutSqliteOrderByFailure()
    {
        var accountId = Guid.NewGuid();
        var older = DateTimeOffset.UtcNow.AddMinutes(-10);
        var newer = older.AddMinutes(5);
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();

        await using (var db = await _database.ContextFactory.CreateDbContextAsync())
        {
            db.Add(new AccountRecord(
                accountId,
                1,
                "catalog-test@example.test",
                "catalog-test@example.test",
                "Catalog test",
                "catalog test",
                null,
                null,
                "openrouter",
                "openrouter",
                false,
                "Connected",
                "Unknown",
                "unknown",
                older,
                "synthetic",
                older,
                older,
                null,
                "test"));
            db.Add(new ProviderConnectionRecord(
                olderId,
                accountId,
                "openrouter",
                ConnectionKind.ApiKey,
                "Older",
                null,
                ConnectionStatus.Authorized,
                older,
                older));
            db.Add(new ProviderCredentialReferenceRecord(
                Guid.NewGuid(),
                olderId,
                "openrouter",
                "cred-older",
                ProviderCredentialKind.ApiKey,
                "connections/openrouter/older",
                null,
                older,
                older));

            db.Add(new ProviderConnectionRecord(
                newerId,
                accountId,
                "openrouter",
                ConnectionKind.ApiKey,
                "Newer",
                null,
                ConnectionStatus.CredentialStored,
                newer,
                newer));
            db.Add(new ProviderCredentialReferenceRecord(
                Guid.NewGuid(),
                newerId,
                "openrouter",
                "cred-newer",
                ProviderCredentialKind.ApiKey,
                "connections/openrouter/newer",
                null,
                newer,
                newer));
            await db.SaveChangesAsync();
        }

        var catalog = new SqliteProviderConnectionCatalog(_database.ContextFactory, null!, null!);

        var result = await catalog.GetEligibleAsync();

        Assert.Equal(new[] { olderId, newerId }, result.Select(item => item.ConnectionId));
    }

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();
}
