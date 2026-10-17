using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Accounts.Persistence;

public sealed class AccountModelContributor : IFlashAiModelContributor
{
    public void Configure(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<AccountRecord>();
        account.ToTable("AccountProfiles");
        account.HasKey(x => x.Id);
        account.Property(x => x.Email).HasMaxLength(254).IsRequired();
        account.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        account.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        account.Property(x => x.NormalizedDisplayName).HasMaxLength(100).IsRequired();
        account.Property(x => x.Notes).HasMaxLength(1000);
        account.Property(x => x.NormalizedNotes).HasMaxLength(1000);
        account.Property(x => x.Provider).HasMaxLength(100).IsRequired();
        account.Property(x => x.NormalizedProvider).HasMaxLength(100).IsRequired();
        account.Property(x => x.ConnectionStatus).HasMaxLength(40).IsRequired();
        account.Property(x => x.RecordedPlan).HasMaxLength(40).IsRequired();
        account.Property(x => x.NormalizedRecordedPlan).HasMaxLength(40).IsRequired();
        account.Property(x => x.DataSource).HasMaxLength(40).IsRequired();
        account.Property(x => x.RecordedBy).HasMaxLength(100).IsRequired();
        account.Property(x => x.LifecycleStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        account.Property(x => x.Version).IsConcurrencyToken().IsRequired();

        account.HasIndex(x => x.NumericId).IsUnique();
        account.HasIndex(x => new { x.NormalizedEmail, x.NormalizedProvider }).IsUnique();
        account.HasIndex(x => x.NormalizedDisplayName);
        account.HasIndex(x => new { x.IsArchived, x.TrackingDateTicksUtc });

        var tag = modelBuilder.Entity<AccountTagRecord>();
        tag.ToTable("AccountTags");
        tag.HasKey(x => new { x.AccountId, x.Value });
        tag.Property(x => x.Value).HasMaxLength(50).IsRequired();
        tag.Property(x => x.NormalizedValue).HasMaxLength(50).IsRequired();
        tag.HasIndex(x => x.NormalizedValue);
        tag.HasOne<AccountRecord>()
            .WithMany(x => x.Tags)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
