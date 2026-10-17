namespace FlashAI.Modules.Subscriptions.Contracts;

public sealed record SubscriptionObservationHistoryItemVm(
    DateTimeOffset RecordedAt,
    string Plan,
    string Source,
    string? Notes);

public sealed record SubscriptionObservationVm(
    Guid AccountId,
    string Plan,
    string Provenance,
    DateTimeOffset ObservedAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? ExpiryTrackingDate,
    string? Notes,
    IReadOnlyList<SubscriptionObservationHistoryItemVm> History);

public sealed record ProviderObservationCardVm(
    Guid ConnectionId,
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
