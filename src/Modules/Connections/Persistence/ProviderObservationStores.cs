using System.Collections.Concurrent;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Persistence;

public sealed class InMemoryProviderObservationStore : IProviderObservationWriter, IProviderObservationReader
{
    private readonly ConcurrentDictionary<Guid, ProviderCheckRunSnapshot> _runs = new();
    private readonly ConcurrentDictionary<Guid, List<ProviderObservationSnapshot>> _observations = new();

    public Task<ProviderCheckRunSummary> SaveAsync(ProviderCheckResult result, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runId = Guid.NewGuid();
        var run = new ProviderCheckRunSnapshot(runId, result.ConnectionId, result.ProviderId, result.Status, result.SafeErrorCode, result.SafeUserMessage, result.CorrelationId, result.StartedAtUtc, result.CompletedAtUtc);
        _runs[runId] = run;
        var snapshots = result.Observations.Select(observation => new ProviderObservationSnapshot(
            Guid.NewGuid(), runId, result.ConnectionId, result.ProviderId, observation.Capability, observation.MetricName, observation.Value, observation.Unit, observation.Currency, observation.Source, observation.Scope, observation.ProviderReportedAtUtc, result.CompletedAtUtc, observation.ValidUntilUtc, observation.Status)).ToList();
        _observations[runId] = snapshots;
        return Task.FromResult(new ProviderCheckRunSummary(runId, result.ConnectionId, result.ProviderId, result.Status, result.SafeErrorCode, result.SafeUserMessage, result.StartedAtUtc, result.CompletedAtUtc, result.Observations));
    }

    public Task<IReadOnlyList<ProviderObservationSnapshot>> GetLatestAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var latestRun = _runs.Values
            .Where(run => run.ConnectionId == connectionId)
            .Where(run => _observations.TryGetValue(run.Id, out var observations) && observations.Count > 0)
            .OrderByDescending(run => run.CompletedAtUtc)
            .FirstOrDefault();
        return Task.FromResult<IReadOnlyList<ProviderObservationSnapshot>>(latestRun is null ? [] : _observations[latestRun.Id]);
    }

    public Task<IReadOnlyList<ProviderCheckRunSnapshot>> GetHistoryAsync(Guid connectionId, int limit = 25, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProviderCheckRunSnapshot>>(_runs.Values.Where(run => run.ConnectionId == connectionId).OrderByDescending(run => run.CompletedAtUtc).Take(Math.Clamp(limit, 1, 200)).ToArray());
    }
}

public sealed class SqliteProviderObservationStore : IProviderObservationWriter, IProviderObservationReader
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;

    public SqliteProviderObservationStore(IDbContextFactory<FlashAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ProviderCheckRunSummary> SaveAsync(ProviderCheckResult result, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runId = Guid.NewGuid();
        db.Add(new ProviderCheckRunRecord(runId, result.ConnectionId, result.ProviderId, result.Status, result.SafeErrorCode, result.SafeUserMessage, result.CorrelationId, result.StartedAtUtc, result.CompletedAtUtc));
        foreach (var observation in result.Observations)
        {
            db.Add(new ProviderObservationRecord(Guid.NewGuid(), runId, result.ConnectionId, result.ProviderId, observation.Capability, observation.MetricName, observation.Value, observation.Unit, observation.Currency, observation.Source, observation.Scope, observation.ProviderReportedAtUtc, result.CompletedAtUtc, observation.ValidUntilUtc, observation.Status));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProviderCheckRunSummary(runId, result.ConnectionId, result.ProviderId, result.Status, result.SafeErrorCode, result.SafeUserMessage, result.StartedAtUtc, result.CompletedAtUtc, result.Observations);
    }

    public async Task<IReadOnlyList<ProviderObservationSnapshot>> GetLatestAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var candidateRuns = await db.Set<ProviderCheckRunRecord>()
            .AsNoTracking()
            .Where(run => run.ConnectionId == connectionId)
            .Where(run => db.Set<ProviderObservationRecord>().Any(observation => observation.CheckRunId == run.Id))
            .Select(run => new { run.Id, run.CompletedAtUtc })
            .ToArrayAsync(cancellationToken);
        var latestRunId = candidateRuns
            .OrderByDescending(run => run.CompletedAtUtc)
            .Select(run => (Guid?)run.Id)
            .FirstOrDefault();
        if (latestRunId is null)
        {
            return [];
        }

        return await db.Set<ProviderObservationRecord>().AsNoTracking().Where(item => item.CheckRunId == latestRunId.Value).OrderBy(item => item.MetricName).Select(ToSnapshot()).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderCheckRunSnapshot>> GetHistoryAsync(Guid connectionId, int limit = 25, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var boundedLimit = Math.Clamp(limit, 1, 200);
        var runs = await db.Set<ProviderCheckRunRecord>()
            .AsNoTracking()
            .Where(run => run.ConnectionId == connectionId)
            .Select(run => new ProviderCheckRunSnapshot(run.Id, run.ConnectionId, run.ProviderId, run.Status, run.SafeErrorCode, run.SafeUserMessage, run.CorrelationId, run.StartedAtUtc, run.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);
        return runs.OrderByDescending(run => run.CompletedAtUtc).Take(boundedLimit).ToArray();
    }

    private static System.Linq.Expressions.Expression<Func<ProviderObservationRecord, ProviderObservationSnapshot>> ToSnapshot()
        => item => new ProviderObservationSnapshot(item.Id, item.CheckRunId, item.ConnectionId, item.ProviderId, item.Capability, item.MetricName, item.Value, item.Unit, item.Currency, item.Source, item.Scope, item.ProviderReportedAtUtc, item.ObservedAtUtc, item.ValidUntilUtc, item.Status);
}
