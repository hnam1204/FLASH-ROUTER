using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.IntegrationTests;

public sealed class AccountUsageRefreshTests
{
    [Fact]
    public async Task RefreshesAuthorizedApiConnectionsWithoutOpeningManualChatGpt()
    {
        var accountId = Guid.NewGuid();
        var manual = Connection(accountId, "openai-chatgpt", ConnectionKind.Manual, ProviderCredentialKind.ManualWebCredential);
        var api = Connection(accountId, "openrouter", ConnectionKind.ApiKey, ProviderCredentialKind.ApiKey);
        var orchestrator = new FakeSyncOrchestrator();
        var service = new AccountUsageRefreshService(
            new FakeConnectionsReader(manual, api),
            orchestrator);

        var result = await service.RefreshAsync(accountId);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.CheckedConnections);
        Assert.Equal(1, result.SkippedConnections);
        Assert.Single(orchestrator.ConnectionIds);
        Assert.Equal(api.ConnectionId, orchestrator.ConnectionIds[0]);
        Assert.True(orchestrator.LastAllowBillableRead);
        Assert.Contains(ProviderCapability.Usage, orchestrator.LastCapabilities!);
        Assert.Contains(ProviderCapability.Quota, orchestrator.LastCapabilities!);
    }

    [Fact]
    public async Task ManualOnlyAccountReportsUnsupportedWithoutCallingOrchestrator()
    {
        var accountId = Guid.NewGuid();
        var orchestrator = new FakeSyncOrchestrator();
        var service = new AccountUsageRefreshService(
            new FakeConnectionsReader(Connection(accountId, "openai-chatgpt", ConnectionKind.Manual, ProviderCredentialKind.ManualWebCredential)),
            orchestrator);

        var result = await service.RefreshAsync(accountId);

        Assert.Equal("Unsupported", result.Status);
        Assert.Empty(orchestrator.ConnectionIds);
    }

    [Fact]
    public async Task SameAccountRefreshDoesNotRunConcurrently()
    {
        var accountId = Guid.NewGuid();
        var orchestrator = new BlockingSyncOrchestrator();
        var service = new AccountUsageRefreshService(
            new FakeConnectionsReader(Connection(accountId, "openrouter", ConnectionKind.ApiKey, ProviderCredentialKind.ApiKey)),
            orchestrator);

        var first = service.RefreshAsync(accountId);
        await orchestrator.Entered.Task;

        var second = await service.RefreshAsync(accountId);

        Assert.Equal("AlreadyRunning", second.Status);
        Assert.True(second.IsAlreadyRunning);
        orchestrator.Release.TrySetResult();
        var firstResult = await first;
        Assert.Equal("Completed", firstResult.Status);
    }

    private static AccountConnectionSummary Connection(
        Guid accountId,
        string providerId,
        ConnectionKind kind,
        ProviderCredentialKind credentialKind,
        ConnectionStatus status = ConnectionStatus.Authorized)
        => new(
            accountId,
            Guid.NewGuid(),
            providerId,
            providerId,
            null,
            kind,
            credentialKind,
            status,
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

    private sealed class FakeConnectionsReader(params AccountConnectionSummary[] connections) : IAccountConnectionsReader
    {
        public Task<IReadOnlyList<AccountConnectionSummary>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AccountConnectionSummary>>(connections.Where(item => item.AccountId == accountId).ToArray());
    }

    private class FakeSyncOrchestrator : IProviderSyncOrchestrator
    {
        public List<Guid> ConnectionIds { get; } = [];
        public IReadOnlySet<ProviderCapability>? LastCapabilities { get; protected set; }
        public bool LastAllowBillableRead { get; protected set; }

        public virtual Task<ProviderCheckRunSummary> CheckConnectionAsync(
            Guid connectionId,
            IReadOnlySet<ProviderCapability>? capabilities = null,
            bool allowBillableRead = false,
            CancellationToken cancellationToken = default)
        {
            ConnectionIds.Add(connectionId);
            LastCapabilities = capabilities;
            LastAllowBillableRead = allowBillableRead;
            return Task.FromResult(Summary(connectionId));
        }

        public Task<CheckAllPreview> PreviewCheckAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CheckAllPreview([], new Dictionary<string, IReadOnlySet<ProviderCapability>>(), false, "synthetic"));

        public Task<CheckAllResult> CheckAllAsync(CheckAllRequest request, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CheckAllResult([], false, "synthetic"));

        protected static ProviderCheckRunSummary Summary(Guid connectionId)
            => new(
                Guid.NewGuid(),
                connectionId,
                "synthetic",
                ProviderOperationStatus.Success,
                string.Empty,
                "synthetic",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                []);
    }

    private sealed class BlockingSyncOrchestrator : FakeSyncOrchestrator
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ProviderCheckRunSummary> CheckConnectionAsync(
            Guid connectionId,
            IReadOnlySet<ProviderCapability>? capabilities = null,
            bool allowBillableRead = false,
            CancellationToken cancellationToken = default)
        {
            ConnectionIds.Add(connectionId);
            LastCapabilities = capabilities;
            LastAllowBillableRead = allowBillableRead;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Summary(connectionId);
        }
    }
}
