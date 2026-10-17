using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Domain;

namespace FlashAI.Modules.Connections.Contracts;

public sealed record ProviderUiDescriptor(
    string ProviderId,
    string DisplayName,
    string ConnectionMethod,
    IReadOnlyList<string> SupportedCapabilities,
    string AuthorizationRequirement,
    string LiveStatus,
    string ImplementationState = "Implemented",
    bool OfflineTested = true,
    bool LiveVerified = false,
    bool IsConnectable = true);

public sealed record ProviderConnectionUiSnapshot(
    ProviderConnectionSummary Connection,
    IReadOnlyList<ProviderObservationSnapshot> LatestObservations,
    IReadOnlyList<ProviderCheckRunSnapshot> RecentChecks);

public interface IProviderUiPort
{
    Task<IReadOnlyList<ProviderUiDescriptor>> GetProvidersAsync(CancellationToken cancellationToken = default);

    Task<ProviderConnectionSummary> AddApiConnectionAsync(
        Guid accountId,
        string providerId,
        ProviderCredentialKind credentialKind,
        string credentialValue,
        string displayLabel,
        string? scope = null,
        CancellationToken cancellationToken = default);

    Task<ProviderCheckRunSummary> CheckConnectionAsync(
        Guid connectionId,
        IReadOnlySet<ProviderCapability>? capabilities = null,
        bool allowBillableRead = false,
        CancellationToken cancellationToken = default);

    Task<ProviderConnectionUiSnapshot?> GetConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default);
}

public enum ConnectionCredentialAvailability
{
    NotApplicable,
    StoredReference,
    MissingReference
}

public enum ConnectionProvenance
{
    OfficialOAuth,
    ProviderApiCredential,
    LocalMetadata
}

public sealed record AccountConnectionSummary(
    Guid AccountId,
    Guid ConnectionId,
    string ProviderId,
    string ProviderDisplayName,
    Guid? RegistrationId,
    ConnectionKind ConnectionKind,
    ProviderCredentialKind? CredentialKind,
    ConnectionStatus Status,
    string? VerifiedIdentity,
    string? Scope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AuthorizedAtUtc,
    DateTimeOffset? LastVerifiedAtUtc,
    DateTimeOffset? LastCheckedAtUtc,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    ConnectionCredentialAvailability CredentialAvailability,
    ConnectionProvenance Provenance,
    IReadOnlyList<string> GrantedScopes,
    string? UserProvidedIdentity = null);

public interface IAccountConnectionsReader
{
    Task<IReadOnlyList<AccountConnectionSummary>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}

public sealed record ConnectionObservationUiSnapshot(
    Guid AccountId,
    Guid ConnectionId,
    string ProviderId,
    string ProviderDisplayName,
    string MetricName,
    string Value,
    string Unit,
    string? Currency,
    string Scope,
    string Source,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ProviderReportedAtUtc,
    DateTimeOffset? ValidUntilUtc,
    string ObservationStatus,
    string? LastCheckStatus,
    string? LastCheckMessage,
    bool IsStale);

public interface IConnectionObservationsReader
{
    Task<IReadOnlyList<ConnectionObservationUiSnapshot>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
