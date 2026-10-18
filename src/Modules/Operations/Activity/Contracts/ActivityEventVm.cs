namespace FlashAI.Modules.Activity.Contracts;

public sealed record ActivityEventVm(
    Guid EventId,
    Guid? AccountId,
    DateTimeOffset Timestamp,
    string EventType,
    string Severity,
    string Summary,
    string? SafeDetails,
    string? CorrelationId = null,
    Guid? AttemptId = null,
    string? ProviderId = null,
    string? Source = null,
    string? Outcome = null,
    string? ErrorCode = null,
    long? DurationMs = null);
