using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.Modules.Connections.Contracts;

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
    bool IsStale);

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
