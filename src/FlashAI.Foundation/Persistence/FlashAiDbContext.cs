using Microsoft.EntityFrameworkCore;

namespace FlashAI.Foundation.Persistence;

public sealed class FlashAiDbContext : DbContext
{
    private readonly IReadOnlyList<IFlashAiModelContributor> _modelContributors;

    public FlashAiDbContext(
        DbContextOptions<FlashAiDbContext> options,
        IEnumerable<IFlashAiModelContributor> modelContributors)
        : base(options)
    {
        _modelContributors = modelContributors.ToArray();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var contributor in _modelContributors)
        {
            contributor.Configure(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);
    }
}
