using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Activity.Contracts;
using FlashAI.Modules.Activity.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Activity.Application;

public sealed class SqliteActivityUiPort : IActivityUiPort
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;

    public SqliteActivityUiPort(IDbContextFactory<FlashAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<ActivityEventVm>> GetEventsAsync(
        Guid? accountId = null,
        string? severity = null,
        CancellationToken cancellationToken = default,
        Guid? attemptId = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Set<ActivityEventRecord>().AsNoTracking().AsQueryable();
        if (accountId.HasValue)
        {
            query = query.Where(item => item.AccountId == accountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(severity) && !severity.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(item => item.Severity == severity);
        }

        if (attemptId.HasValue)
        {
            query = query.Where(item => item.AttemptId == attemptId.Value);
        }

        var events = await query
            .Select(item => new ActivityEventVm(
                item.Id,
                item.AccountId,
                item.OccurredAtUtc,
                item.EventType,
                item.Severity,
                item.SafeSummary,
                item.SafeDetails,
                item.CorrelationId,
                item.AttemptId,
                item.ProviderId,
                item.Source,
                item.Outcome,
                item.ErrorCode,
                item.DurationMs))
            .ToArrayAsync(cancellationToken);

        // SQLite cannot translate DateTimeOffset ordering; order the bounded UI projection in memory.
        return events
            .OrderByDescending(item => item.Timestamp)
            .ThenByDescending(item => item.EventId)
            .Take(200)
            .ToArray();
    }
}
