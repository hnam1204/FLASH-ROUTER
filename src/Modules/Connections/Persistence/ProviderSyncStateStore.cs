using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Persistence;

public sealed class SqliteProviderSyncStateStore : IProviderSyncStateStore
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;

    public SqliteProviderSyncStateStore(IDbContextFactory<FlashAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<ProviderSyncStateSnapshot?> GetAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<Domain.ProviderSyncStateRecord>()
            .AsNoTracking()
            .Where(item => item.ConnectionId == connectionId)
            .Select(item => new ProviderSyncStateSnapshot(
                item.Enabled,
                item.NextEligibleAtUtc,
                item.LastSuccessfulSyncAtUtc,
                item.FailureCount,
                item.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(
        Guid connectionId,
        DateTimeOffset? nextEligibleAtUtc,
        DateTimeOffset? lastSuccessfulSyncAtUtc,
        int failureCount,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var state = await context.Set<Domain.ProviderSyncStateRecord>()
            .SingleOrDefaultAsync(item => item.ConnectionId == connectionId, cancellationToken);
        if (state is null)
        {
            return false;
        }

        state.UpdateSchedule(nextEligibleAtUtc, lastSuccessfulSyncAtUtc, failureCount, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
