using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashAI.IntegrationTests;

public sealed class ProviderSyncSchedulerTests
{
    [Fact]
    public void DefaultsToSixtySecondTicksWithoutEnablingBillableReads()
    {
        var options = new ProviderSyncOptions();

        Assert.Equal(TimeSpan.FromSeconds(60), options.Interval);
        Assert.False(options.AllowOfficialUsageReads);
    }

    [Fact]
    public async Task ChecksOnlyEnabledConnectionsAndPersistsNextEligibleState()
    {
        var connectionId = Guid.NewGuid();
        var stateStore = new FakeSyncStateStore(new ProviderSyncStateSnapshot(
            Enabled: true,
            NextEligibleAtUtc: null,
            LastSuccessfulSyncAtUtc: null,
            FailureCount: 0,
            UpdatedAtUtc: DateTimeOffset.UtcNow));
        var orchestrator = new FakeSyncOrchestrator(
            new CheckAllPreview(
                [new ProviderConnectionSummary(
                    connectionId,
                    Guid.NewGuid(),
                    "openai-chatgpt",
                    "Synthetic connection",
                    ConnectionKind.ApiKey,
                    ProviderCredentialKind.ApiKey,
                    ConnectionStatus.CredentialStored,
                    "cred_synthetic",
                    DateTimeOffset.UtcNow)],
                new Dictionary<string, IReadOnlySet<ProviderCapability>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["openai-chatgpt"] = new HashSet<ProviderCapability>
                    {
                        ProviderCapability.ConnectionHealth,
                        ProviderCapability.Usage
                    }
                },
                HasUnknownBillingRisk: true,
                SafeMessage: "synthetic"),
            ProviderOperationStatus.Success);

        await RunUntilCheckedAsync(orchestrator, stateStore);

        Assert.Equal(1, orchestrator.CheckCount);
        Assert.NotNull(orchestrator.LastCapabilities);
        Assert.Contains(ProviderCapability.ConnectionHealth, orchestrator.LastCapabilities!);
        Assert.DoesNotContain(ProviderCapability.Usage, orchestrator.LastCapabilities!);
        Assert.False(orchestrator.LastAllowBillableRead);
        Assert.NotNull(stateStore.Current.NextEligibleAtUtc);
        Assert.Equal(0, stateStore.Current.FailureCount);
        Assert.NotNull(stateStore.Current.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public async Task RateLimitPersistsFailureBackoffAndPreventsImmediateOverlap()
    {
        var connectionId = Guid.NewGuid();
        var stateStore = new FakeSyncStateStore(new ProviderSyncStateSnapshot(
            Enabled: true,
            NextEligibleAtUtc: null,
            LastSuccessfulSyncAtUtc: null,
            FailureCount: 0,
            UpdatedAtUtc: DateTimeOffset.UtcNow));
        var orchestrator = new FakeSyncOrchestrator(
            new CheckAllPreview(
                [new ProviderConnectionSummary(
                    connectionId,
                    Guid.NewGuid(),
                    "openai-chatgpt",
                    "Synthetic connection",
                    ConnectionKind.ApiKey,
                    ProviderCredentialKind.ApiKey,
                    ConnectionStatus.CredentialStored,
                    "cred_synthetic",
                    DateTimeOffset.UtcNow)],
                new Dictionary<string, IReadOnlySet<ProviderCapability>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["openai-chatgpt"] = new HashSet<ProviderCapability> { ProviderCapability.ConnectionHealth }
                },
                false,
                "synthetic"),
            ProviderOperationStatus.RateLimited);

        await RunUntilCheckedAsync(orchestrator, stateStore);

        Assert.Equal(1, orchestrator.CheckCount);
        Assert.Equal(1, stateStore.Current.FailureCount);
        Assert.True(stateStore.Current.NextEligibleAtUtc > DateTimeOffset.UtcNow.AddSeconds(30));
        Assert.Null(stateStore.Current.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public async Task CompletesThreeSyntheticCyclesWithoutOverlapOrBillableUsageReads()
    {
        var connectionId = Guid.NewGuid();
        var stateStore = new FakeSyncStateStore(new ProviderSyncStateSnapshot(
            Enabled: true,
            NextEligibleAtUtc: null,
            LastSuccessfulSyncAtUtc: null,
            FailureCount: 0,
            UpdatedAtUtc: DateTimeOffset.UtcNow));
        var orchestrator = new FakeSyncOrchestrator(
            new CheckAllPreview(
                [new ProviderConnectionSummary(
                    connectionId,
                    Guid.NewGuid(),
                    "openai-chatgpt",
                    "Synthetic connection",
                    ConnectionKind.ApiKey,
                    ProviderCredentialKind.ApiKey,
                    ConnectionStatus.CredentialStored,
                    "cred_synthetic",
                    DateTimeOffset.UtcNow)],
                new Dictionary<string, IReadOnlySet<ProviderCapability>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["openai-chatgpt"] = new HashSet<ProviderCapability>
                    {
                        ProviderCapability.ConnectionHealth,
                        ProviderCapability.Usage
                    }
                },
                HasUnknownBillingRisk: true,
                SafeMessage: "synthetic"),
            ProviderOperationStatus.Success);

        var scheduler = new ProviderSyncScheduler(
            orchestrator,
            stateStore,
            new ProviderSyncOptions
            {
                Enabled = true,
                Interval = TimeSpan.FromMilliseconds(25)
            },
            NullLogger<ProviderSyncScheduler>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await scheduler.StartAsync(cancellation.Token);
        while (orchestrator.CheckCount < 3 && !cancellation.IsCancellationRequested)
        {
            await Task.Delay(10, cancellation.Token);
        }

        await scheduler.StopAsync(CancellationToken.None);

        Assert.True(orchestrator.CheckCount >= 3);
        Assert.Equal(1, orchestrator.MaximumConcurrentChecks);
        Assert.NotNull(orchestrator.LastCapabilities);
        Assert.Contains(ProviderCapability.ConnectionHealth, orchestrator.LastCapabilities!);
        Assert.DoesNotContain(ProviderCapability.Usage, orchestrator.LastCapabilities!);
        Assert.False(orchestrator.LastAllowBillableRead);
        Assert.True(stateStore.Updates.Count >= 3);
    }

    private static async Task RunUntilCheckedAsync(FakeSyncOrchestrator orchestrator, FakeSyncStateStore stateStore)
    {
        var scheduler = new ProviderSyncScheduler(
            orchestrator,
            stateStore,
            new ProviderSyncOptions
            {
                Enabled = true,
                Interval = TimeSpan.FromMilliseconds(100)
            },
            NullLogger<ProviderSyncScheduler>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await scheduler.StartAsync(cancellation.Token);
        while (orchestrator.CheckCount == 0 && !cancellation.IsCancellationRequested)
        {
            await Task.Delay(10, cancellation.Token);
        }

        await scheduler.StopAsync(CancellationToken.None);
        Assert.NotEmpty(stateStore.Updates);
    }

    private sealed class FakeSyncStateStore : IProviderSyncStateStore
    {
        public FakeSyncStateStore(ProviderSyncStateSnapshot current)
        {
            Current = current;
        }

        public ProviderSyncStateSnapshot Current { get; private set; }
        public List<ProviderSyncStateSnapshot> Updates { get; } = [];

        public Task<ProviderSyncStateSnapshot?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProviderSyncStateSnapshot?>(Current);

        public Task<bool> UpdateAsync(
            Guid connectionId,
            DateTimeOffset? nextEligibleAtUtc,
            DateTimeOffset? lastSuccessfulSyncAtUtc,
            int failureCount,
            CancellationToken cancellationToken = default)
        {
            Current = Current with
            {
                NextEligibleAtUtc = nextEligibleAtUtc,
                LastSuccessfulSyncAtUtc = lastSuccessfulSyncAtUtc,
                FailureCount = failureCount,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            Updates.Add(Current);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSyncOrchestrator : IProviderSyncOrchestrator
    {
        private readonly CheckAllPreview _preview;
        private readonly ProviderOperationStatus _status;

        public FakeSyncOrchestrator(CheckAllPreview preview, ProviderOperationStatus status)
        {
            _preview = preview;
            _status = status;
        }

        public int CheckCount { get; private set; }
        public int MaximumConcurrentChecks { get; private set; }
        public IReadOnlySet<ProviderCapability>? LastCapabilities { get; private set; }
        public bool LastAllowBillableRead { get; private set; }

        private int _activeChecks;

        public Task<CheckAllPreview> PreviewCheckAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_preview);

        public Task<ProviderCheckRunSummary> CheckConnectionAsync(
            Guid connectionId,
            IReadOnlySet<ProviderCapability>? capabilities = null,
            bool allowBillableRead = false,
            CancellationToken cancellationToken = default)
        {
            CheckCount++;
            var activeChecks = Interlocked.Increment(ref _activeChecks);
            MaximumConcurrentChecks = Math.Max(MaximumConcurrentChecks, activeChecks);
            LastCapabilities = capabilities;
            LastAllowBillableRead = allowBillableRead;
            try
            {
                return Task.FromResult(new ProviderCheckRunSummary(
                    Guid.NewGuid(),
                    connectionId,
                    "openai-chatgpt",
                    _status,
                    _status == ProviderOperationStatus.Success ? string.Empty : "RATE_LIMITED",
                    "synthetic",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    []));
            }
            finally
            {
                Interlocked.Decrement(ref _activeChecks);
            }
        }

        public Task<CheckAllResult> CheckAllAsync(
            CheckAllRequest request,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new CheckAllResult([], false, "synthetic"));
    }
}
