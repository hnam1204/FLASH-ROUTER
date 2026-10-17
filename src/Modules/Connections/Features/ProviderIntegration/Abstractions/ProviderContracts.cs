using FlashAI.Modules.Connections.Domain;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

public enum ProviderType
{
    AiApi,
    AiWeb
}

public enum ProviderImplementationState
{
    Implemented,
    ResearchOnly
}

public enum ProviderCredentialKind
{
    ApiKey,
    AdminApiKey,
    ProjectApiKey,
    ManualWebCredential,
    OAuthAccessToken,
    OAuthRefreshToken
}

public enum ProviderCapability
{
    Identity,
    ConnectionHealth,
    AvailableModels,
    Subscription,
    Usage,
    TokenUsage,
    Costs,
    Credits,
    Balance,
    RateLimits,
    Quota,
    BillingPeriod
}

public enum ProviderOperationStatus
{
    Success,
    PartialSuccess,
    Unauthorized,
    Forbidden,
    RateLimited,
    Timeout,
    NetworkFailure,
    ProviderUnavailable,
    Unsupported,
    InvalidResponse,
    Cancelled,
    ConfigurationError
}

public enum ProviderObservationStatus
{
    Confirmed,
    UserProvided,
    Derived,
    Stale,
    Unsupported,
    Unavailable,
    Error
}

public enum ProviderObservationScope
{
    IndividualKey,
    Project,
    Workspace,
    Organization,
    BillingAccount,
    Model,
    Unknown
}

public enum ProviderObservationSource
{
    OfficialApi,
    CalculatedFromOfficialObservations,
    UserProvided,
    LocalMetadata
}

public sealed record ProviderDescriptor(
    string ProviderId,
    string DisplayName,
    ProviderType ProviderType,
    ConnectionKind ConnectionKind,
    ProviderCredentialKind RequiredCredentialKind,
    IReadOnlySet<ProviderCapability> SupportedCapabilities,
    string DocumentationReference,
    string AuthorizationRequirement,
    IReadOnlySet<string> EndpointAllowlist,
    ProviderImplementationState ImplementationState = ProviderImplementationState.Implemented,
    bool OfflineTested = true,
    bool LiveVerified = false,
    bool IsConnectable = true);

public sealed record ProviderConnectionContext(
    Guid ConnectionId,
    Guid AccountId,
    string ProviderId,
    ConnectionKind ConnectionKind,
    ProviderCredentialKind CredentialKind,
    string CredentialId,
    ConnectionStatus Status,
    IReadOnlySet<string> AuthorizedScopes,
    string? Scope,
    DateTimeOffset CreatedAtUtc);

public sealed record ProviderCredential(
    string ProviderId,
    Guid ConnectionId,
    string CredentialId,
    ProviderCredentialKind CredentialKind,
    string Value,
    string? Scope);

public sealed record ProviderCheckRequest(
    IReadOnlySet<ProviderCapability> Capabilities,
    DateTimeOffset? StartUtc = null,
    DateTimeOffset? EndUtc = null,
    bool AllowBillableRead = false);

public sealed record ProviderObservationDraft(
    ProviderCapability Capability,
    string MetricName,
    string Value,
    string Unit,
    string? Currency,
    ProviderObservationSource Source,
    ProviderObservationScope Scope,
    DateTimeOffset? ProviderReportedAtUtc,
    DateTimeOffset? ValidUntilUtc,
    ProviderObservationStatus Status = ProviderObservationStatus.Confirmed);

public sealed record ProviderOperationResult(
    ProviderOperationStatus Status,
    string SafeErrorCode,
    string SafeUserMessage,
    string ProviderId,
    Guid ConnectionId,
    ProviderCapability? Capability,
    DateTimeOffset ObservedAtUtc,
    string CorrelationId,
    DateTimeOffset? RetryAfterUtc = null,
    IReadOnlyList<ProviderObservationDraft>? Observations = null)
{
    public bool IsSuccess => Status == ProviderOperationStatus.Success;

    public IReadOnlyList<ProviderObservationDraft> SafeObservations => Observations ?? [];

    public static ProviderOperationResult Success(
        string providerId,
        Guid connectionId,
        DateTimeOffset observedAtUtc,
        string correlationId,
        IReadOnlyList<ProviderObservationDraft> observations,
        ProviderCapability? capability = null)
        => new(
            ProviderOperationStatus.Success,
            string.Empty,
            string.Empty,
            providerId,
            connectionId,
            capability,
            observedAtUtc,
            correlationId,
            Observations: observations);

    public static ProviderOperationResult Failure(
        ProviderOperationStatus status,
        string code,
        string message,
        ProviderConnectionContext context,
        DateTimeOffset observedAtUtc,
        string correlationId,
        ProviderCapability? capability = null,
        DateTimeOffset? retryAfterUtc = null,
        IReadOnlyList<ProviderObservationDraft>? observations = null)
        => new(
            status,
            code,
            message,
            context.ProviderId,
            context.ConnectionId,
            capability,
            observedAtUtc,
            correlationId,
            retryAfterUtc,
            observations);
}

public sealed record ProviderCheckResult(
    ProviderOperationStatus Status,
    string SafeErrorCode,
    string SafeUserMessage,
    string ProviderId,
    Guid ConnectionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string CorrelationId,
    IReadOnlyList<ProviderObservationDraft> Observations,
    IReadOnlyList<ProviderOperationResult> Operations)
{
    public bool IsSuccess => Status == ProviderOperationStatus.Success;
}

public sealed record ProviderCheckRunSummary(
    Guid CheckRunId,
    Guid ConnectionId,
    string ProviderId,
    ProviderOperationStatus Status,
    string SafeErrorCode,
    string SafeUserMessage,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<ProviderObservationDraft> Observations);

public sealed record ProviderObservationSnapshot(
    Guid Id,
    Guid CheckRunId,
    Guid ConnectionId,
    string ProviderId,
    ProviderCapability Capability,
    string MetricName,
    string Value,
    string Unit,
    string? Currency,
    ProviderObservationSource Source,
    ProviderObservationScope Scope,
    DateTimeOffset? ProviderReportedAtUtc,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ValidUntilUtc,
    ProviderObservationStatus Status);

public sealed record ProviderCheckRunSnapshot(
    Guid Id,
    Guid ConnectionId,
    string ProviderId,
    ProviderOperationStatus Status,
    string SafeErrorCode,
    string SafeUserMessage,
    string CorrelationId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record ProviderConnectionCreationRequest(
    Guid AccountId,
    string ProviderId,
    ProviderCredentialKind CredentialKind,
    string CredentialValue,
    string DisplayLabel,
    string? Scope = null,
    IReadOnlySet<string>? AuthorizedScopes = null);

public sealed record ProviderConnectionSummary(
    Guid ConnectionId,
    Guid AccountId,
    string ProviderId,
    string DisplayLabel,
    ConnectionKind ConnectionKind,
    ProviderCredentialKind CredentialKind,
    ConnectionStatus Status,
    string CredentialId,
    DateTimeOffset CreatedAtUtc);

public sealed record CheckAllPreview(
    IReadOnlyList<ProviderConnectionSummary> EligibleConnections,
    IReadOnlyDictionary<string, IReadOnlySet<ProviderCapability>> CapabilitiesByProvider,
    bool HasUnknownBillingRisk,
    string SafeMessage);

public sealed record CheckAllRequest(
    IReadOnlySet<ProviderCapability>? Capabilities = null,
    bool Confirmed = false,
    bool AllowBillableRead = false);

public sealed record CheckAllResult(
    IReadOnlyList<ProviderCheckRunSummary> Results,
    bool IsCancelled,
    string SafeMessage);

public interface IAiProviderAdapter
{
    ProviderDescriptor Descriptor { get; }

    Task<ProviderCheckResult> CheckAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCheckRequest request,
        CancellationToken cancellationToken = default);
}

public interface IProviderRegistry
{
    IReadOnlyList<ProviderDescriptor> Descriptors { get; }

    IAiProviderAdapter Resolve(string providerId);

    bool TryResolve(string providerId, out IAiProviderAdapter? adapter);
}

public interface IProviderConnectionVerifier
{
    Task<ProviderCheckResult> VerifyAsync(
        ProviderConnectionContext connection,
        ProviderCheckRequest request,
        CancellationToken cancellationToken = default);
}

public interface IProviderCapabilityResolver
{
    IReadOnlySet<ProviderCapability> Resolve(ProviderConnectionContext connection, ProviderDescriptor descriptor);
}

public interface IProviderObservationReader
{
    Task<IReadOnlyList<ProviderObservationSnapshot>> GetLatestAsync(Guid connectionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderCheckRunSnapshot>> GetHistoryAsync(Guid connectionId, int limit = 25, CancellationToken cancellationToken = default);
}

public interface IProviderObservationWriter
{
    Task<ProviderCheckRunSummary> SaveAsync(
        ProviderCheckResult result,
        CancellationToken cancellationToken = default);
}

public interface IProviderCredentialResolver
{
    Task<ProviderCredential?> ResolveAsync(ProviderConnectionContext connection, CancellationToken cancellationToken = default);

    Task StoreAsync(
        ProviderConnectionContext connection,
        string credentialValue,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(ProviderConnectionContext connection, CancellationToken cancellationToken = default);
}

public interface IProviderSyncOrchestrator
{
    Task<ProviderCheckRunSummary> CheckConnectionAsync(
        Guid connectionId,
        IReadOnlySet<ProviderCapability>? capabilities = null,
        bool allowBillableRead = false,
        CancellationToken cancellationToken = default);

    Task<CheckAllPreview> PreviewCheckAllAsync(CancellationToken cancellationToken = default);

    Task<CheckAllResult> CheckAllAsync(
        CheckAllRequest request,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IProviderErrorMapper
{
    ProviderOperationResult Map(
        ProviderConnectionContext connection,
        ProviderCapability? capability,
        Exception exception,
        DateTimeOffset observedAtUtc,
        string correlationId);
}

public interface IProviderConnectionCatalog
{
    Task<ProviderConnectionSummary> CreateApiConnectionAsync(
        ProviderConnectionCreationRequest request,
        CancellationToken cancellationToken = default);

    Task<ProviderConnectionContext?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderConnectionSummary>> GetEligibleAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderConnectionSummary>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task MarkStatusAsync(Guid connectionId, ConnectionStatus status, CancellationToken cancellationToken = default);
}

public sealed class ProviderOperationException : Exception
{
    public ProviderOperationException(ProviderOperationResult result)
        : base(result.SafeUserMessage)
    {
        Result = result;
    }

    public ProviderOperationResult Result { get; }
}
