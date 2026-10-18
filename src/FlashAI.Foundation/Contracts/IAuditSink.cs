namespace FlashAI.Foundation.Contracts;

public interface IAuditSink
{
    ValueTask WriteAsync(
        Guid? accountId,
        string eventType,
        string safeSummary,
        string severity = "Info",
        string? safeDetails = null,
        string? correlationId = null,
        Guid? attemptId = null,
        string? providerId = null,
        string? source = null,
        string? outcome = null,
        string? errorCode = null,
        long? durationMs = null,
        CancellationToken cancellationToken = default);
}
