using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.Modules.Connections.Contracts;

public enum AccountUsageWindowType
{
    Unknown,
    FiveHours,
    Week,
    BillingPeriod,
    Rolling,
    Other
}

public enum UsageIdentityBindingStatus
{
    Unavailable,
    ConnectionScoped,
    ProviderIdentityVerified,
    Mismatch
}

public sealed record AccountUsageSnapshot(
    Guid AccountId,
    Guid ConnectionId,
    string ProviderId,
    ProviderCapability Capability,
    string Scope,
    string Status,
    decimal? UsedValue,
    decimal? LimitValue,
    decimal? RemainingValue,
    decimal? UsagePercent,
    string? Unit,
    DateTimeOffset? ResetAtUtc,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? NextCheckAtUtc,
    string? EvidenceSource,
    string? SanitizedFailureCode,
    bool IsStale,
    string CapabilityStatus = "Supported",
    string? ProductId = null,
    AccountUsageWindowType WindowType = AccountUsageWindowType.Unknown,
    DateTimeOffset? WindowStartedAtUtc = null,
    decimal? UsedPercentage = null,
    decimal? RemainingPercentage = null,
    string? VerifiedAccountIdentity = null,
    UsageIdentityBindingStatus IdentityBindingStatus = UsageIdentityBindingStatus.Unavailable,
    string? PlanType = null,
    long? WindowDurationSeconds = null);

public sealed record AccountUsageView(
    Guid AccountId,
    IReadOnlyList<AccountUsageSnapshot> Snapshots,
    DateTimeOffset ObservedAtUtc);

public interface IAccountUsageReader
{
    Task<AccountUsageView> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}

public sealed record AccountUsageRefreshResult(
    Guid AccountId,
    string Status,
    string Message,
    int CheckedConnections,
    int SkippedConnections,
    bool IsAlreadyRunning = false,
    int FailedConnections = 0);

public interface IAccountUsageRefreshPort
{
    Task<AccountUsageRefreshResult> RefreshAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}

public interface ICodexCliUsageRefreshPort
{
    Task<AccountUsageRefreshResult> RefreshAsync(
        Guid accountId,
        string verifiedAccountIdentity,
        CancellationToken cancellationToken = default);
}
