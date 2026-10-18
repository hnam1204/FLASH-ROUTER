using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.IntegrationTests;

public sealed class AccountUsageReaderTests
{
    [Fact]
    public async Task ParsesOfficialUsageValuesAndPreservesUnknownQuotaAsNull()
    {
        var accountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var resetAt = DateTimeOffset.UtcNow.AddHours(4);
        var connection = Connection(accountId, connectionId, "openrouter", ConnectionKind.ApiKey);
        var observations = new FakeObservationReader(
            [
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Usage, "usage", "25", "requests", null, ProviderObservationSource.OfficialApi, ProviderObservationScope.IndividualKey, observedAt, observedAt, null, ProviderObservationStatus.Confirmed),
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Usage, "limit", "100", "requests", null, ProviderObservationSource.OfficialApi, ProviderObservationScope.IndividualKey, observedAt, observedAt, null, ProviderObservationStatus.Confirmed),
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Usage, "limit_reset", resetAt.ToString("O"), "timestamp", null, ProviderObservationSource.OfficialApi, ProviderObservationScope.IndividualKey, observedAt, observedAt, null, ProviderObservationStatus.Confirmed),
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Costs, "cost", "1.25", "currency_amount", "USD", ProviderObservationSource.OfficialApi, ProviderObservationScope.Organization, observedAt, observedAt, null, ProviderObservationStatus.Confirmed)
            ]);
        var reader = new AccountUsageReader(
            new FakeConnectionsReader(connection),
            observations,
            new FakeSyncStateReader(),
            new FakeRegistry());

        var result = await reader.GetAsync(accountId);

        var usage = Assert.Single(result.Snapshots, snapshot => snapshot.Capability == ProviderCapability.Usage);
        Assert.Equal("Available", usage.Status);
        Assert.Equal(25m, usage.UsedValue);
        Assert.Equal(100m, usage.LimitValue);
        Assert.Null(usage.RemainingValue);
        Assert.Equal(25m, usage.UsagePercent);
        Assert.Equal("requests", usage.Unit);
        Assert.NotNull(usage.ResetAtUtc);
        Assert.True(Math.Abs((usage.ResetAtUtc.Value - resetAt).TotalSeconds) < 1);
        Assert.Equal(ProviderObservationSource.OfficialApi.ToString(), usage.EvidenceSource?.Split(';')[0]);

        var costs = Assert.Single(result.Snapshots, snapshot => snapshot.Capability == ProviderCapability.Costs);
        Assert.Null(costs.LimitValue);
        Assert.Null(costs.UsagePercent);
    }

    [Fact]
    public async Task ReportsManualChatGptWebUsageAsUnsupportedWithoutFabricatedValues()
    {
        var accountId = Guid.NewGuid();
        var connection = Connection(accountId, Guid.NewGuid(), "openai-chatgpt", ConnectionKind.Manual);
        var reader = new AccountUsageReader(
            new FakeConnectionsReader(connection),
            new FakeObservationReader([]),
            new FakeSyncStateReader(),
            new FakeRegistry());

        var result = await reader.GetAsync(accountId);

        var snapshot = Assert.Single(result.Snapshots);
        Assert.Equal("Unsupported", snapshot.Status);
        Assert.Null(snapshot.UsedValue);
        Assert.Null(snapshot.LimitValue);
        Assert.Null(snapshot.RemainingValue);
        Assert.Null(snapshot.UsagePercent);
        Assert.Equal("USAGE_SOURCE_UNSUPPORTED", snapshot.SanitizedFailureCode);
    }

    private static AccountConnectionSummary Connection(Guid accountId, Guid connectionId, string providerId, ConnectionKind kind)
        => new(
            accountId,
            connectionId,
            providerId,
            providerId,
            null,
            kind,
            kind == ConnectionKind.Manual ? ProviderCredentialKind.ManualWebCredential : ProviderCredentialKind.ApiKey,
            ConnectionStatus.Authorized,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            ConnectionCredentialAvailability.StoredReference,
            kind == ConnectionKind.Manual ? ConnectionProvenance.LocalMetadata : ConnectionProvenance.ProviderApiCredential,
            []);

    private sealed class FakeConnectionsReader(AccountConnectionSummary connection) : IAccountConnectionsReader
    {
        public Task<IReadOnlyList<AccountConnectionSummary>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AccountConnectionSummary>>(connection.AccountId == accountId ? [connection] : []);
    }

    private sealed class FakeObservationReader(IReadOnlyList<ProviderObservationSnapshot> latest) : IProviderObservationReader
    {
        public Task<IReadOnlyList<ProviderObservationSnapshot>> GetLatestAsync(Guid connectionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProviderObservationSnapshot>>(latest);

        public Task<IReadOnlyList<ProviderCheckRunSnapshot>> GetHistoryAsync(Guid connectionId, int limit = 25, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProviderCheckRunSnapshot>>([]);
    }

    private sealed class FakeSyncStateReader : IProviderSyncStateStore
    {
        public Task<ProviderSyncStateSnapshot?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProviderSyncStateSnapshot?>(null);

        public Task<bool> UpdateAsync(Guid connectionId, DateTimeOffset? nextEligibleAtUtc, DateTimeOffset? lastSuccessfulSyncAtUtc, int failureCount, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakeRegistry : IProviderRegistry
    {
        public IReadOnlyList<ProviderDescriptor> Descriptors { get; } =
        [
            new ProviderDescriptor(
                "openrouter",
                "OpenRouter",
                ProviderType.AiApi,
                ConnectionKind.ApiKey,
                ProviderCredentialKind.ApiKey,
                new HashSet<ProviderCapability> { ProviderCapability.Usage, ProviderCapability.Costs },
                "https://openrouter.ai/docs",
                "API key",
                new HashSet<string> { "openrouter.ai" })
        ];

        public IAiProviderAdapter Resolve(string providerId) => throw new NotSupportedException();

        public bool TryResolve(string providerId, out IAiProviderAdapter? adapter)
        {
            adapter = null;
            return false;
        }
    }
}
