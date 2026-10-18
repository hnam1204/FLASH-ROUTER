using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Domain;
using FlashAI.Modules.Accounts.Persistence;
using FlashAI.Modules.Activity.Persistence;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class Be07DataAccuracyTests
{
    [Fact]
    public async Task InMemoryLatestSnapshotSurvivesFailedCheck()
    {
        var store = new InMemoryProviderObservationStore();
        var connectionId = Guid.NewGuid();
        var success = CreateResult(connectionId, ProviderOperationStatus.Success, [CreateObservation()]);
        var failed = CreateResult(connectionId, ProviderOperationStatus.RateLimited, []);

        await store.SaveAsync(success);
        await store.SaveAsync(failed);

        var latest = await store.GetLatestAsync(connectionId);

        var observation = Assert.Single(latest);
        Assert.Equal("input_tokens", observation.MetricName);
        Assert.Equal("100", observation.Value);
    }

    [Fact]
    public async Task SqliteLatestSnapshotSurvivesFailedCheck()
    {
        var root = Path.Combine(WorkspaceRoot(), ".local", "test-data", "be07-accuracy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            var paths = new LocalDataPaths(root);
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
            await using var provider = services.BuildServiceProvider();
            var connections = provider.GetRequiredService<SqliteConnectionFactory>();
            await connections.PrepareDatabaseAsync();
            await provider.GetRequiredService<MigrationCoordinator>().MigrateAsync();

            var store = new SqliteProviderObservationStore(provider.GetRequiredService<IDbContextFactory<FlashAiDbContext>>());
            var connectionId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await using (var db = await provider.GetRequiredService<IDbContextFactory<FlashAiDbContext>>().CreateDbContextAsync())
            {
                db.Add(new AccountRecord(
                    accountId,
                    1,
                    "be07-accuracy@example.test",
                    "BE07-ACCURACY@EXAMPLE.TEST",
                    "BE-07 accuracy",
                    "BE-07 ACCURACY",
                    null,
                    null,
                    "openai-platform",
                    "OPENAI-PLATFORM",
                    false,
                    "Unverified",
                    "Unknown",
                    "UNKNOWN",
                    now,
                    "Test",
                    now,
                    now,
                    null,
                    "BE-07"));
                db.Add(new ProviderConnectionRecord(
                    connectionId,
                    accountId,
                    "openai-platform",
                    ConnectionKind.ApiKey,
                    "BE-07 accuracy connection",
                    null,
                    ConnectionStatus.NotConfigured,
                    now,
                    now));
                await db.SaveChangesAsync();
            }

            await store.SaveAsync(CreateResult(connectionId, ProviderOperationStatus.Success, [CreateObservation()]));
            await store.SaveAsync(CreateResult(connectionId, ProviderOperationStatus.RateLimited, []));

            var latest = await store.GetLatestAsync(connectionId);

            var observation = Assert.Single(latest);
            Assert.Equal("input_tokens", observation.MetricName);
            Assert.Equal("100", observation.Value);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ProviderCheckResult CreateResult(
        Guid connectionId,
        ProviderOperationStatus status,
        IReadOnlyList<ProviderObservationDraft> observations)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return new ProviderCheckResult(
            status,
            status == ProviderOperationStatus.Success ? string.Empty : "PROVIDER_RATE_LIMITED",
            status == ProviderOperationStatus.Success ? string.Empty : "The provider is rate limited.",
            "openai-platform",
            connectionId,
            completedAt.AddSeconds(-1),
            completedAt,
            Guid.NewGuid().ToString("N"),
            observations,
            []);
    }

    private static ProviderObservationDraft CreateObservation()
        => new(
            ProviderCapability.TokenUsage,
            "input_tokens",
            "100",
            "tokens",
            null,
            ProviderObservationSource.OfficialApi,
            ProviderObservationScope.Organization,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null);

    private static string WorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the solution root.");
    }
}
