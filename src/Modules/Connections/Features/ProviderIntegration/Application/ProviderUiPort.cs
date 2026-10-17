using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Application;

public sealed class ProviderUiPort : IProviderUiPort
{
    private readonly IProviderRegistry _registry;
    private readonly IProviderConnectionCatalog _connections;
    private readonly IProviderSyncOrchestrator _orchestrator;
    private readonly IProviderObservationReader _observations;

    public ProviderUiPort(
        IProviderRegistry registry,
        IProviderConnectionCatalog connections,
        IProviderSyncOrchestrator orchestrator,
        IProviderObservationReader observations)
    {
        _registry = registry;
        _connections = connections;
        _orchestrator = orchestrator;
        _observations = observations;
    }

    public Task<IReadOnlyList<ProviderUiDescriptor>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProviderUiDescriptor>>(_registry.Descriptors.Select(descriptor => new ProviderUiDescriptor(
            descriptor.ProviderId,
            descriptor.DisplayName,
            descriptor.ConnectionKind.ToString(),
            descriptor.SupportedCapabilities.OrderBy(value => value).Select(value => value.ToString()).ToArray(),
            descriptor.AuthorizationRequirement,
            descriptor.ImplementationState == ProviderImplementationState.ResearchOnly
                ? "Research metadata only; no adapter or live request is enabled"
                : "Offline contract ready; live request requires explicit user action",
            descriptor.ImplementationState.ToString(),
            descriptor.OfflineTested,
            descriptor.LiveVerified,
            descriptor.IsConnectable)).ToArray());
    }

    public Task<ProviderConnectionSummary> AddApiConnectionAsync(
        Guid accountId,
        string providerId,
        ProviderCredentialKind credentialKind,
        string credentialValue,
        string displayLabel,
        string? scope = null,
        CancellationToken cancellationToken = default)
        => _connections.CreateApiConnectionAsync(new ProviderConnectionCreationRequest(accountId, providerId, credentialKind, credentialValue, displayLabel, scope), cancellationToken);

    public Task<ProviderCheckRunSummary> CheckConnectionAsync(
        Guid connectionId,
        IReadOnlySet<ProviderCapability>? capabilities = null,
        bool allowBillableRead = false,
        CancellationToken cancellationToken = default)
        => _orchestrator.CheckConnectionAsync(connectionId, capabilities, allowBillableRead, cancellationToken);

    public async Task<ProviderConnectionUiSnapshot?> GetConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetAsync(connectionId, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        var summary = (await _connections.GetEligibleAsync(cancellationToken)).SingleOrDefault(item => item.ConnectionId == connectionId)
            ?? new ProviderConnectionSummary(connection.ConnectionId, connection.AccountId, connection.ProviderId, connection.ProviderId, connection.ConnectionKind, connection.CredentialKind, connection.Status, connection.CredentialId, connection.CreatedAtUtc);
        return new ProviderConnectionUiSnapshot(
            summary,
            await _observations.GetLatestAsync(connectionId, cancellationToken),
            await _observations.GetHistoryAsync(connectionId, 10, cancellationToken));
    }
}
