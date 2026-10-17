namespace FlashAI.Modules.Activity.Contracts;

public sealed record ActivityEventVm(
    Guid EventId,
    Guid? AccountId,
    DateTimeOffset Timestamp,
    string EventType,
    string Severity,
    string Summary,
    string? SafeDetails);
