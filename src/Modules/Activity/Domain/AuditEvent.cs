namespace FlashAI.Modules.Activity.Domain;

public sealed record AuditEvent(
    Guid Id,
    string EventType,
    string SafeSummary,
    string CorrelationId,
    DateTimeOffset OccurredAt);
