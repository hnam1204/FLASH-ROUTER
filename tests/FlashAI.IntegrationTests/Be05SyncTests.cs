using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.CredentialResolution;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;
using FlashAI.Modules.Connections.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace FlashAI.IntegrationTests;

public sealed class Be05SyncTests
{
    [Fact]
    public async Task ManualCheckStoresObservationsAndMapsConnectionStatus()
    {
        using var fixture = Fixture.Create();
        var connection = await fixture.Catalog.CreateApiConnectionAsync(new ProviderConnectionCreationRequest(
            Guid.NewGuid(),
            "synthetic",
            ProviderCredentialKind.AdminApiKey,
            "synthetic-secret",
            "Synthetic provider"));

        var summary = await fixture.Orchestrator.CheckConnectionAsync(connection.ConnectionId);
        Assert.Equal(ProviderOperationStatus.Success, summary.Status);
        Assert.Single(await fixture.Observations.GetLatestAsync(connection.ConnectionId));
        Assert.Equal(ConnectionStatus.Authorized, (await fixture.Catalog.GetAsync(connection.ConnectionId))!.Status);
    }

    [Fact]
    public async Task CheckAllRequiresExplicitConfirmationAndHonorsCancellation()
    {
        using var fixture = Fixture.Create();
        await fixture.Catalog.CreateApiConnectionAsync(new ProviderConnectionCreationRequest(Guid.NewGuid(), "synthetic", ProviderCredentialKind.AdminApiKey, "secret-a", "A"));
        await fixture.Catalog.CreateApiConnectionAsync(new ProviderConnectionCreationRequest(Guid.NewGuid(), "synthetic", ProviderCredentialKind.AdminApiKey, "secret-b", "B"));

        var notConfirmed = await fixture.Orchestrator.CheckAllAsync(new CheckAllRequest());
        Assert.False(notConfirmed.IsCancelled);
        Assert.Empty(notConfirmed.Results);
        Assert.Contains("confirmation", notConfirmed.SafeMessage, StringComparison.OrdinalIgnoreCase);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await fixture.Orchestrator.CheckAllAsync(new CheckAllRequest(Confirmed: true), cancellationToken: cancellation.Token);
        Assert.True(cancelled.IsCancelled);
        Assert.Empty(cancelled.Results);
    }

    [Fact]
    public async Task CredentialVaultFailurePreventsProviderCallAndPreservesSafeStatus()
    {
        using var fixture = Fixture.Create();
        var connection = await fixture.Catalog.CreateApiConnectionAsync(new ProviderConnectionCreationRequest(Guid.NewGuid(), "synthetic", ProviderCredentialKind.AdminApiKey, "secret", "Synthetic"));
        await fixture.Credentials.RemoveAsync((await fixture.Catalog.GetAsync(connection.ConnectionId))!);

        var summary = await fixture.Orchestrator.CheckConnectionAsync(connection.ConnectionId);
        Assert.Equal(ProviderOperationStatus.ConfigurationError, summary.Status);
        Assert.Equal("CREDENTIAL_NOT_FOUND", summary.SafeErrorCode);
        Assert.Equal(ConnectionStatus.Error, (await fixture.Catalog.GetAsync(connection.ConnectionId))!.Status);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;

        private Fixture(
            string root,
            ProviderCredentialVaultStore credentials,
            InMemoryProviderConnectionCatalog catalog,
            InMemoryProviderObservationStore observations,
            ProviderSyncOrchestrator orchestrator)
        {
            _root = root;
            Credentials = credentials;
            Catalog = catalog;
            Observations = observations;
            Orchestrator = orchestrator;
        }

        public ProviderCredentialVaultStore Credentials { get; }
        public InMemoryProviderConnectionCatalog Catalog { get; }
        public InMemoryProviderObservationStore Observations { get; }
        public ProviderSyncOrchestrator Orchestrator { get; }

        public static Fixture Create()
        {
            var root = Path.Combine(WorkspaceRoot(), ".local", "test-data", "be05-sync-" + Guid.NewGuid().ToString("N"));
            var dataProtection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys")), builder => builder.SetApplicationName("FLASH ROUTER BE-05 TEST"));
            var vault = new FlashAI.Foundation.Security.FileSecretVault(Path.Combine(root, "vault"), dataProtection);
            var credentials = new ProviderCredentialVaultStore(vault);
            var adapter = new SyntheticAdapter();
            var registry = new ProviderRegistry([adapter]);
            var catalog = new InMemoryProviderConnectionCatalog(credentials, registry);
            var observations = new InMemoryProviderObservationStore();
            var clock = new SystemClock();
            var verifier = new ProviderConnectionVerifier(registry, new DefaultProviderCapabilityResolver(), credentials, clock);
            var orchestrator = new ProviderSyncOrchestrator(catalog, verifier, observations, registry, new DefaultProviderCapabilityResolver(), new ProviderRateLimiter());
            return new Fixture(root, credentials, catalog, observations, orchestrator);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

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

    private sealed class SyntheticAdapter : IAiProviderAdapter
    {
        public ProviderDescriptor Descriptor { get; } = new(
            "synthetic",
            "Synthetic Provider",
            ProviderType.AiApi,
            ConnectionKind.ApiKey,
            ProviderCredentialKind.AdminApiKey,
            new HashSet<ProviderCapability> { ProviderCapability.ConnectionHealth, ProviderCapability.Costs },
            "offline://synthetic",
            "Synthetic offline contract",
            new HashSet<string> { "synthetic.invalid" });

        public Task<ProviderCheckResult> CheckAsync(ProviderConnectionContext connection, ProviderCredential credential, ProviderCheckRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var observations = request.Capabilities.Select(capability => new ProviderObservationDraft(capability, capability == ProviderCapability.Costs ? "cost" : "reachable", capability == ProviderCapability.Costs ? "1.25" : "true", capability == ProviderCapability.Costs ? "currency_amount" : "boolean", capability == ProviderCapability.Costs ? "USD" : null, ProviderObservationSource.OfficialApi, ProviderObservationScope.Organization, now, null)).ToArray();
            return Task.FromResult(new ProviderCheckResult(ProviderOperationStatus.Success, string.Empty, string.Empty, connection.ProviderId, connection.ConnectionId, now, now, Guid.NewGuid().ToString("N"), observations, []));
        }
    }
}
