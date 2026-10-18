using System.Collections.Concurrent;
using FlashAI.Modules.Activity.Contracts;

namespace FlashAI.Modules.Activity.Application;

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly ConcurrentQueue<(Guid? AccountId, string EventType, string Summary)> _events = new();
    private readonly IActivityEventStream? _eventStream;

    public InMemoryAuditSink(IActivityEventStream? eventStream = null)
    {
        _eventStream = eventStream;
    }

    public ValueTask WriteAsync(
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
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Enqueue((accountId, eventType, safeSummary));
        _eventStream?.Publish(new ActivityEventVm(
            Guid.NewGuid(), accountId, DateTimeOffset.UtcNow, eventType, severity, safeSummary, safeDetails,
            correlationId, attemptId, providerId, source, outcome, errorCode, durationMs));
        return ValueTask.CompletedTask;
    }
}
