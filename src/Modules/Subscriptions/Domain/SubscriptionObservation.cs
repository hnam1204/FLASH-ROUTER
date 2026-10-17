namespace FlashAI.Modules.Subscriptions.Domain;

public sealed record SubscriptionObservation(
    Guid AccountId,
    string? UserLabel,
    string Source,
    DateTimeOffset ObservedAt,
    string Confidence,
    DateTimeOffset? ExpiresAt);
