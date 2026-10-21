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
        Assert.Equal(25m, usage.UsedPercentage);
        Assert.Null(usage.RemainingPercentage);
        Assert.Equal("requests", usage.Unit);
        Assert.Equal("openrouter", usage.ProductId);
        Assert.Equal(UsageIdentityBindingStatus.ConnectionScoped, usage.IdentityBindingStatus);
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

    [Fact]
    public async Task ProjectsAuthenticatedChatGptUsageApiObservationIntoRealQuotaWindows()
    {
        var accountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        const string providerEmail = "verified@example.test";
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var connection = Connection(accountId, connectionId, "openai-chatgpt", ConnectionKind.Manual) with
        {
            VerifiedIdentity = providerEmail
        };
        var apiSnapshot = new ChatGptWebUsageSnapshot(
            "free",
            new ChatGptUsageWindowSnapshot(27.5m, 18_000, observedAt.AddHours(4), 14_400),
            new ChatGptUsageWindowSnapshot(42m, 604_800, observedAt.AddDays(3), 259_200),
            observedAt);
        var observation = new ProviderAccountObservationSnapshot(
            Guid.NewGuid(),
            accountId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "openai-chatgpt",
            "verified-browser-context",
            observedAt,
            "en",
            true,
            false,
            "Observed",
            "Official ChatGPT account settings UI and authenticated usage API",
            null,
            [
                new ProviderAccountFieldEvidence(ProviderAccountObservationField.Email, providerEmail, "OfficialSettingsAccount", "OfficialSettingsAccount", "High", ProviderAccountObservationFieldStatus.Observed, observedAt),
                new ProviderAccountFieldEvidence(ProviderAccountObservationField.UsageQuota, ChatGptWebUsageSnapshotCodec.Serialize(apiSnapshot), "GET https://chatgpt.com/backend-api/wham/usage", "ChatGptWebUsageV1", "Verified session", ProviderAccountObservationFieldStatus.Observed, observedAt)
            ]);
        var reader = new AccountUsageReader(
            new FakeConnectionsReader(connection),
            new FakeObservationReader([]),
            new FakeSyncStateReader(),
            new FakeRegistry(),
            new FakeAccountObservationReader(observation));

        var result = await reader.GetAsync(accountId);

        Assert.Equal(2, result.Snapshots.Count);
        var primary = Assert.Single(result.Snapshots, item => item.Scope.EndsWith("PrimaryWindow", StringComparison.Ordinal));
        Assert.Equal("Available", primary.Status);
        Assert.Equal(27.5m, primary.UsedValue);
        Assert.Equal(100m, primary.LimitValue);
        Assert.Equal(72.5m, primary.RemainingValue);
        Assert.Equal("percent", primary.Unit);
        Assert.Equal(AccountUsageWindowType.FiveHours, primary.WindowType);
        Assert.Equal(18_000, primary.WindowDurationSeconds);
        Assert.Equal("free", primary.PlanType);
        Assert.Equal(providerEmail, primary.VerifiedAccountIdentity);
        Assert.Equal(UsageIdentityBindingStatus.ProviderIdentityVerified, primary.IdentityBindingStatus);

        var secondary = Assert.Single(result.Snapshots, item => item.Scope.EndsWith("SecondaryWindow", StringComparison.Ordinal));
        Assert.Equal(42m, secondary.UsedValue);
        Assert.Equal(58m, secondary.RemainingValue);
        Assert.Equal(AccountUsageWindowType.Week, secondary.WindowType);
        Assert.Equal(604_800, secondary.WindowDurationSeconds);
    }

    [Fact]
    public async Task ProjectsUsageOnlySnapshotBoundToVerifiedSelectedAccountWithoutStoringEmail()
    {
        var accountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var connection = Connection(accountId, connectionId, "openai-chatgpt", ConnectionKind.Manual);
        var apiSnapshot = new ChatGptWebUsageSnapshot(
            "free",
            new ChatGptUsageWindowSnapshot(3m, 18_000, null, 14_400),
            new ChatGptUsageWindowSnapshot(0m, 604_800, observedAt.AddDays(6), 518_400),
            observedAt);
        var observation = new ProviderAccountObservationSnapshot(
            Guid.NewGuid(), accountId, Guid.NewGuid(), Guid.NewGuid(), "openai-chatgpt",
            "account-bound-context", observedAt, "en", true, false, "UsageObserved",
            "Phiên ChatGPT đã xác minh", null,
            [
                new ProviderAccountFieldEvidence(ProviderAccountObservationField.Email, null,
                    "Phiên ChatGPT đã đối chiếu với account đang chọn", "VerifiedChatGptUsageBindingV1",
                    "Đã khớp account", ProviderAccountObservationFieldStatus.Observed, observedAt),
                new ProviderAccountFieldEvidence(ProviderAccountObservationField.UsageQuota,
                    ChatGptWebUsageSnapshotCodec.Serialize(apiSnapshot), "ChatGPT", "ChatGptWebUsageV1",
                    "Đã xác minh phiên", ProviderAccountObservationFieldStatus.Observed, observedAt)
            ]);
        var reader = new AccountUsageReader(
            new FakeConnectionsReader(connection),
            new FakeObservationReader([]),
            new FakeSyncStateReader(),
            new FakeRegistry(),
            new FakeAccountObservationReader(observation));

        var result = await reader.GetAsync(accountId);

        Assert.Equal(2, result.Snapshots.Count);
        Assert.All(result.Snapshots, snapshot =>
        {
            Assert.Equal("free", snapshot.PlanType);
            Assert.Null(snapshot.VerifiedAccountIdentity);
            Assert.Equal(UsageIdentityBindingStatus.ProviderIdentityVerified, snapshot.IdentityBindingStatus);
        });
        var primary = Assert.Single(result.Snapshots, item => item.Scope.EndsWith("PrimaryWindow", StringComparison.Ordinal));
        Assert.Equal(97m, primary.RemainingValue);
        Assert.Equal(observedAt.AddHours(4), primary.ResetAtUtc);
    }

    [Fact]
    public async Task DoesNotProjectUnboundLocalCodexSessionOntoSelectedAccount()
    {
        var accountId = Guid.NewGuid();
        var connection = Connection(accountId, Guid.NewGuid(), "openai-chatgpt", ConnectionKind.Manual);
        var reader = new AccountUsageReader(
            new FakeConnectionsReader(connection),
            new FakeObservationReader([]),
            new FakeSyncStateReader(),
            new FakeRegistry(),
            new FakeAccountObservationReader(null));

        var result = await reader.GetAsync(accountId);

        Assert.DoesNotContain(result.Snapshots, item => item.ProductId == "CodexCLI");
    }

    [Fact]
    public async Task MapsCodexQuotaOnlyFromSelectedAccountConnectionAndActualWindowFields()
    {
        var accountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow;
        var connection = Connection(accountId, connectionId, "codex-cli", ConnectionKind.WebOAuth) with
        {
            VerifiedIdentity = "owner@example.test"
        };
        var observations = new FakeObservationReader(
            [
                CodexObservation("plan_type", "free"),
                CodexObservation("used_percent", "0"),
                CodexObservation("limit_window_seconds", "18000"),
                CodexObservation("reset_after_seconds", "7200")
            ]);
        var reader = new AccountUsageReader(
            new FakeConnectionsReader(connection), observations, new FakeSyncStateReader(), new FakeRegistry());

        var result = await reader.GetAsync(accountId);

        var quota = Assert.Single(result.Snapshots);
        Assert.Equal("CodexCLI", quota.ProductId);
        Assert.Equal(accountId, quota.AccountId);
        Assert.Equal("free", quota.PlanType);
        Assert.Equal(18_000, quota.WindowDurationSeconds);
        Assert.Equal(AccountUsageWindowType.FiveHours, quota.WindowType);
        Assert.Equal(0m, quota.UsedPercentage);
        Assert.Equal(100m, quota.RemainingPercentage);
        Assert.Equal("owner@example.test", quota.VerifiedAccountIdentity);
        Assert.Equal(UsageIdentityBindingStatus.ProviderIdentityVerified, quota.IdentityBindingStatus);
        Assert.Equal(observedAt.AddSeconds(7200), quota.ResetAtUtc);

        ProviderObservationSnapshot CodexObservation(string metric, string value)
            => new(Guid.NewGuid(), Guid.NewGuid(), connectionId, "codex-cli", ProviderCapability.Quota,
                metric, value, "percent", null, ProviderObservationSource.OfficialApi,
                ProviderObservationScope.Workspace, observedAt, observedAt, null, ProviderObservationStatus.Confirmed);
    }

    [Fact]
    public async Task MarksExpiredProviderObservationAsStaleAndPreservesReportedRemaining()
    {
        var accountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var connection = Connection(accountId, connectionId, "openrouter", ConnectionKind.ApiKey);
        var observations = new FakeObservationReader(
            [
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Quota, "usage", "75", "requests", null, ProviderObservationSource.OfficialApi, ProviderObservationScope.IndividualKey, observedAt, observedAt, observedAt.AddMinutes(1), ProviderObservationStatus.Confirmed),
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Quota, "limit", "100", "requests", null, ProviderObservationSource.OfficialApi, ProviderObservationScope.IndividualKey, observedAt, observedAt, observedAt.AddMinutes(1), ProviderObservationStatus.Confirmed),
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Quota, "limit_remaining", "25", "requests", null, ProviderObservationSource.OfficialApi, ProviderObservationScope.IndividualKey, observedAt, observedAt, observedAt.AddMinutes(1), ProviderObservationStatus.Confirmed)
            ]);
        var reader = new AccountUsageReader(
            new FakeConnectionsReader(connection),
            observations,
            new FakeSyncStateReader(),
            new FakeRegistry());

        var result = await reader.GetAsync(accountId);
        var snapshot = Assert.Single(result.Snapshots, item => item.Capability == ProviderCapability.Quota);

        Assert.Equal("Stale", snapshot.Status);
        Assert.True(snapshot.IsStale);
        Assert.Equal(75m, snapshot.UsedValue);
        Assert.Equal(100m, snapshot.LimitValue);
        Assert.Equal(25m, snapshot.RemainingValue);
    }

    [Fact]
    public async Task DoesNotCalculatePercentageAcrossIncompatibleUnits()
    {
        var accountId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var observedAt = DateTimeOffset.UtcNow;
        var connection = Connection(accountId, connectionId, "openrouter", ConnectionKind.ApiKey);
        var observations = new FakeObservationReader(
            [
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Quota, "usage", "25", "requests", null, ProviderObservationSource.OfficialApi, ProviderObservationScope.IndividualKey, observedAt, observedAt, null, ProviderObservationStatus.Confirmed),
                new ProviderObservationSnapshot(Guid.NewGuid(), Guid.NewGuid(), connectionId, "openrouter", ProviderCapability.Quota, "limit", "100", "tokens", null, ProviderObservationSource.OfficialApi, ProviderObservationScope.IndividualKey, observedAt, observedAt, null, ProviderObservationStatus.Confirmed)
            ]);
        var reader = new AccountUsageReader(
            new FakeConnectionsReader(connection),
            observations,
            new FakeSyncStateReader(),
            new FakeRegistry());

        var result = await reader.GetAsync(accountId);
        var snapshot = Assert.Single(result.Snapshots, item => item.Capability == ProviderCapability.Quota);

        Assert.Null(snapshot.UsagePercent);
        Assert.Null(snapshot.UsedPercentage);
        Assert.Null(snapshot.RemainingPercentage);
        Assert.Equal("USAGE_PERCENTAGE_UNAVAILABLE_UNIT_MISMATCH", snapshot.SanitizedFailureCode);
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

    private sealed class FakeAccountObservationReader(ProviderAccountObservationSnapshot? latest) : IProviderAccountObservationReader
    {
        public Task<ProviderAccountObservationSnapshot?> GetLatestAsync(
            Guid accountId,
            string providerId = "openai-chatgpt",
            CancellationToken cancellationToken = default)
            => Task.FromResult(latest?.AccountId == accountId
                && latest.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                    ? latest
                    : null);
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
