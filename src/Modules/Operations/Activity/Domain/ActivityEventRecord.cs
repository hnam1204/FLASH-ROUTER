namespace FlashAI.Modules.Activity.Domain;

public sealed class ActivityEventRecord
{
    private ActivityEventRecord()
    {
    }

    public ActivityEventRecord(
        Guid id,
        Guid? accountId,
        string eventType,
        string severity,
        string safeSummary,
        string? safeDetails,
        string correlationId,
        DateTimeOffset occurredAtUtc,
        Guid? attemptId = null,
        string? providerId = null,
        string? source = null,
        string? outcome = null,
        string? errorCode = null,
        long? durationMs = null)
    {
        Id = id;
        AccountId = accountId;
        EventType = eventType;
        Severity = severity;
        SafeSummary = safeSummary;
        SafeDetails = safeDetails;
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc;
        AttemptId = attemptId;
        ProviderId = providerId;
        Source = source;
        Outcome = outcome;
        ErrorCode = errorCode;
        DurationMs = durationMs;
    }

    public Guid Id { get; private set; }
    public Guid? AccountId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string SafeSummary { get; private set; } = string.Empty;
    public string? SafeDetails { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public Guid? AttemptId { get; private set; }
    public string? ProviderId { get; private set; }
    public string? Source { get; private set; }
    public string? Outcome { get; private set; }
    public string? ErrorCode { get; private set; }
    public long? DurationMs { get; private set; }
}
