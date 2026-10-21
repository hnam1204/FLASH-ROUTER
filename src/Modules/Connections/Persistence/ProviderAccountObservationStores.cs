using System.Collections.Concurrent;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Persistence;

public sealed class InMemoryProviderAccountObservationStore : IProviderAccountObservationStore, IProviderAccountObservationReader
{
    private readonly ConcurrentDictionary<(Guid AccountId, string ProviderId), ProviderAccountObservationSnapshot> _latest = new();

    public Task SaveAsync(ProviderAccountObservation observation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = ToSnapshot(Guid.NewGuid(), observation);
        _latest[(observation.AccountId, observation.ProviderId)] = snapshot;
        return Task.CompletedTask;
    }

    public Task<ProviderAccountObservationSnapshot?> GetLatestAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _latest.TryGetValue((accountId, providerId), out var snapshot);
        return Task.FromResult(snapshot);
    }

    private static ProviderAccountObservationSnapshot ToSnapshot(Guid id, ProviderAccountObservation observation)
        => new(
            id,
            observation.AccountId,
            observation.AttemptId,
            observation.BrowserProfileBindingId,
            observation.ProviderId,
            observation.BrowserContextId,
            observation.ObservedAtUtc,
            observation.Locale,
            observation.AuthenticatedShellDetected,
            observation.AccountMenuDetected,
            observation.ObservationStatus,
            observation.EvidenceSurface,
            observation.FailureReasonCode,
            [observation.DisplayName, observation.Email, observation.Plan, observation.MfaConfiguration, observation.UsageQuota]);
}

public sealed class SqliteProviderAccountObservationStore : IProviderAccountObservationStore, IProviderAccountObservationReader
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;

    public SqliteProviderAccountObservationStore(IDbContextFactory<FlashAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task SaveAsync(ProviderAccountObservation observation, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var observationId = Guid.NewGuid();
        db.Add(new ProviderAccountObservationRecord(
            observationId,
            observation.AccountId,
            observation.AttemptId,
            observation.BrowserProfileBindingId,
            observation.ProviderId,
            observation.BrowserContextId,
            observation.ObservedAtUtc,
            observation.Locale,
            observation.AuthenticatedShellDetected,
            observation.AccountMenuDetected,
            observation.ObservationStatus,
            observation.EvidenceSurface,
            observation.FailureReasonCode));

        foreach (var field in Fields(observation))
        {
            db.Add(new ProviderAccountObservationFieldRecord(
                Guid.NewGuid(),
                observationId,
                field.Field,
                field.Value,
                field.Source,
                field.EvidenceType,
                field.Confidence,
                field.Status,
                field.ObservedAtUtc,
                field.FailureReasonCode));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProviderAccountObservationSnapshot?> GetLatestAsync(
        Guid accountId,
        string providerId = "openai-chatgpt",
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.Set<ProviderAccountObservationRecord>()
            .AsNoTracking()
            .Where(item => item.AccountId == accountId && item.ProviderId == providerId)
            .ToArrayAsync(cancellationToken);

        var latest = candidates
            .OrderByDescending(item => item.ObservedAtUtc.UtcDateTime.Ticks)
            .FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        var fields = await db.Set<ProviderAccountObservationFieldRecord>()
            .AsNoTracking()
            .Where(item => item.ObservationId == latest.Id)
            .OrderBy(item => item.Field)
            .Select(item => new ProviderAccountFieldEvidence(
                item.Field,
                item.Value,
                item.Source,
                item.EvidenceType,
                item.Confidence,
                item.Status,
                item.ObservedAtUtc,
                item.FailureReasonCode))
            .ToArrayAsync(cancellationToken);

        return new ProviderAccountObservationSnapshot(
            latest.Id,
            latest.AccountId,
            latest.AttemptId,
            latest.BrowserProfileBindingId,
            latest.ProviderId,
            latest.BrowserContextId,
            latest.ObservedAtUtc,
            latest.Locale,
            latest.AuthenticatedShellDetected,
            latest.AccountMenuDetected,
            latest.ObservationStatus,
            latest.EvidenceSurface,
            latest.FailureReasonCode,
            fields);
    }

    private static IEnumerable<ProviderAccountFieldEvidence> Fields(ProviderAccountObservation observation)
    {
        yield return observation.DisplayName;
        yield return observation.Email;
        yield return observation.Plan;
        yield return observation.MfaConfiguration;
        yield return observation.UsageQuota;
    }
}
