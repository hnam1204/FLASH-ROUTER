using Microsoft.EntityFrameworkCore;

namespace FlashAI.Foundation.Persistence;

public sealed class MigrationCoordinator
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly SqliteConnectionFactory _connections;

    public MigrationCoordinator(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        SqliteConnectionFactory connections)
    {
        _contextFactory = contextFactory;
        _connections = connections;
    }

    public bool IsDurableSchemaEnabled => true;

    public string PhaseStatus => "IMPLEMENTED: one host-owned migration pipeline for Integrated mode.";

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await _connections.PrepareDatabaseAsync(cancellationToken);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
