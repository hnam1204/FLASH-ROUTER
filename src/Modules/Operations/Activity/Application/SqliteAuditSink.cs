using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Activity.Contracts;
using FlashAI.Modules.Activity.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Activity.Application;

public sealed class SqliteAuditSink : IAuditSink
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly IActivityEventStream? _eventStream;

    public SqliteAuditSink(IDbContextFactory<FlashAiDbContext> contextFactory, IActivityEventStream? eventStream = null)
    {
        _contextFactory = contextFactory;
        _eventStream = eventStream;
    }

    public async ValueTask WriteAsync(
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
        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(safeSummary))
        {
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var eventId = Guid.NewGuid();
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var normalizedSeverity = NormalizeSeverity(severity);
        var correlation = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId.Trim();
        context.Add(new ActivityEventRecord(
            eventId,
            accountId,
            eventType.Trim(),
            normalizedSeverity,
            safeSummary.Trim(),
            string.IsNullOrWhiteSpace(safeDetails) ? null : safeDetails.Trim(),
            correlation,
            occurredAtUtc,
            attemptId,
            string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim(),
            string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
            string.IsNullOrWhiteSpace(outcome) ? null : outcome.Trim(),
            string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim(),
            durationMs));
        await context.SaveChangesAsync(cancellationToken);
        _eventStream?.Publish(new ActivityEventVm(
            eventId, accountId, occurredAtUtc, eventType.Trim(), normalizedSeverity,
            safeSummary.Trim(), string.IsNullOrWhiteSpace(safeDetails) ? null : safeDetails.Trim(),
            correlation, attemptId, providerId, source, outcome, errorCode, durationMs));
    }

    private static string NormalizeSeverity(string severity)
        => severity.Trim().ToUpperInvariant() switch
        {
            "SUCCESS" or "PASS" => "Success",
            "WARNING" or "WARN" => "Warning",
            "ERROR" => "Error",
            _ => "Info"
        };
}
