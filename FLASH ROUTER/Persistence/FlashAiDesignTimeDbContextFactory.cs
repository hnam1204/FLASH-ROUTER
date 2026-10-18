using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Persistence;
using FlashAI.Modules.Connections.Persistence;
using FlashAI.Modules.Activity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FLASH_ROUTER.Persistence;

public sealed class FlashAiDesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlashAiDbContext>
{
    public FlashAiDbContext CreateDbContext(string[] args)
    {
        var migrationsAssembly = typeof(FlashAiDesignTimeDbContextFactory).Assembly.GetName().Name!;
        var options = new DbContextOptionsBuilder<FlashAiDbContext>()
            .UseSqlite(
                "Data Source=:memory:",
                sqlite => sqlite.MigrationsAssembly(migrationsAssembly))
            .Options;

        return new FlashAiDbContext(
            options,
            [new AccountModelContributor(), new ConnectionModelContributor(), new ActivityModelContributor()]);
    }
}
