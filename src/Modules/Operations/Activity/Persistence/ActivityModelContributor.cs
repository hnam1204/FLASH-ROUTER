using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Activity.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Activity.Persistence;

public sealed class ActivityModelContributor : IFlashAiModelContributor
{
    public void Configure(ModelBuilder modelBuilder)
    {
        var activity = modelBuilder.Entity<ActivityEventRecord>();
        activity.ToTable("ActivityEvents");
        activity.HasKey(x => x.Id);
        activity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        activity.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        activity.Property(x => x.SafeSummary).HasMaxLength(1000).IsRequired();
        activity.Property(x => x.SafeDetails).HasMaxLength(2000);
        activity.Property(x => x.CorrelationId).HasMaxLength(80).IsRequired();
        activity.Property(x => x.ProviderId).HasMaxLength(100);
        activity.Property(x => x.Source).HasMaxLength(100);
        activity.Property(x => x.Outcome).HasMaxLength(40);
        activity.Property(x => x.ErrorCode).HasMaxLength(120);
        activity.HasIndex(x => new { x.AccountId, x.OccurredAtUtc });
        activity.HasIndex(x => new { x.AccountId, x.AttemptId, x.OccurredAtUtc });
        activity.HasIndex(x => x.CorrelationId);
    }
}
