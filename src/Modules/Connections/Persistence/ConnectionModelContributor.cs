using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Persistence;

public sealed class ConnectionModelContributor : IFlashAiModelContributor
{
    public void Configure(ModelBuilder modelBuilder)
    {
        var connection = modelBuilder.Entity<ProviderConnectionRecord>();
        connection.ToTable("ProviderConnections");
        connection.HasKey(x => x.Id);
        connection.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        connection.Property(x => x.ConnectionKind).HasConversion<string>().HasMaxLength(20).IsRequired();
        connection.Property(x => x.DisplayLabel).HasMaxLength(150).IsRequired();
        connection.Property(x => x.ExternalIdentity).HasMaxLength(254);
        connection.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        connection.HasIndex(x => x.AccountId);
        connection.HasIndex(x => new { x.AccountId, x.ProviderId, x.ConnectionKind, x.ExternalIdentity }).IsUnique();

        var browserBinding = modelBuilder.Entity<BrowserProfileBindingRecord>();
        browserBinding.ToTable("BrowserProfileBindings");
        browserBinding.HasKey(x => x.Id);
        browserBinding.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        browserBinding.Property(x => x.BrowserKind).HasMaxLength(30).IsRequired();
        browserBinding.Property(x => x.OpaqueProfileIdentifier).HasMaxLength(80).IsRequired();
        browserBinding.Property(x => x.ProfileDirectoryReference).HasMaxLength(1000).IsRequired();
        browserBinding.Property(x => x.State).HasMaxLength(30).IsRequired();
        browserBinding.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().IsRequired();
        browserBinding.HasIndex(x => new { x.AccountId, x.ProviderId, x.BrowserKind }).IsUnique();
        browserBinding.HasIndex(x => x.OpaqueProfileIdentifier).IsUnique();

        // Configure the cross-module FK by name so project references remain one-way.
        modelBuilder.Entity("FlashAI.Modules.Connections.Domain.ProviderConnectionRecord")
            .HasOne("FlashAI.Modules.Accounts.Domain.AccountRecord", null)
            .WithMany()
            .HasForeignKey(nameof(ProviderConnectionRecord.AccountId))
            .OnDelete(DeleteBehavior.Cascade);

        var registration = modelBuilder.Entity<OAuthRegistrationRecord>();
        registration.ToTable("OAuthRegistrations");
        registration.HasKey(x => x.Id);
        registration.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        registration.Property(x => x.IssuedClientId).HasMaxLength(200).IsRequired();
        registration.Property(x => x.HostIdentifier).HasMaxLength(300).IsRequired();
        registration.Property(x => x.AgentName).HasMaxLength(150).IsRequired();
        registration.Property(x => x.ProviderSubject).HasMaxLength(300);
        registration.HasIndex(x => new { x.ProviderId, x.IssuedClientId }).IsUnique();
        registration.HasIndex(x => x.AccountId);

        var session = modelBuilder.Entity<ConnectionSessionRecord>();
        session.ToTable("ConnectionSessions");
        session.HasKey(x => x.Id);
        session.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        session.Property(x => x.ProviderSubject).HasMaxLength(300).IsRequired();
        session.Property(x => x.IssuedClientId).HasMaxLength(200).IsRequired();
        session.Property(x => x.HostIdentifier).HasMaxLength(300).IsRequired();
        session.Property(x => x.GrantedScopes).HasMaxLength(1000).IsRequired();
        session.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        session.HasIndex(x => new { x.AccountId, x.ProviderId, x.ProviderSubject, x.IssuedClientId }).IsUnique();
        session.HasIndex(x => x.ConnectionId).IsUnique();

        var tokenReference = modelBuilder.Entity<TokenVaultReferenceRecord>();
        tokenReference.ToTable("TokenVaultReferences");
        tokenReference.HasKey(x => x.Id);
        tokenReference.Property(x => x.AccessTokenKey).HasMaxLength(300).IsRequired();
        tokenReference.Property(x => x.RefreshTokenKey).HasMaxLength(300).IsRequired();
        tokenReference.Property(x => x.IdTokenKey).HasMaxLength(300).IsRequired();
        tokenReference.HasIndex(x => x.ConnectionId).IsUnique();

        var check = modelBuilder.Entity<ConnectionCheckRecord>();
        check.ToTable("ConnectionChecks");
        check.HasKey(x => x.Id);
        check.Property(x => x.CheckType).HasMaxLength(80).IsRequired();
        check.Property(x => x.Source).HasMaxLength(200).IsRequired();
        check.Property(x => x.Result).HasMaxLength(2000).IsRequired();
        check.HasIndex(x => new { x.ConnectionId, x.ObservedAtUtc });

        var credential = modelBuilder.Entity<ProviderCredentialReferenceRecord>();
        credential.ToTable("ProviderCredentialReferences");
        credential.HasKey(x => x.Id);
        credential.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        credential.Property(x => x.CredentialId).HasMaxLength(200).IsRequired();
        credential.Property(x => x.CredentialKind).HasConversion<string>().HasMaxLength(40).IsRequired();
        credential.Property(x => x.VaultKey).HasMaxLength(300).IsRequired();
        credential.Property(x => x.Scope).HasMaxLength(200);
        credential.Property(x => x.ProtectionScheme).HasMaxLength(100).IsRequired();
        credential.Property(x => x.ProtectionFormatVersion).IsRequired();
        credential.HasIndex(x => x.ConnectionId).IsUnique();
        credential.HasIndex(x => new { x.ProviderId, x.CredentialId }).IsUnique();
        credential.HasOne<ProviderConnectionRecord>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        var capability = modelBuilder.Entity<ProviderCapabilityDefinitionRecord>();
        capability.ToTable("ProviderCapabilityDefinitions");
        capability.HasKey(x => x.Id);
        capability.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        capability.Property(x => x.Capability).HasConversion<string>().HasMaxLength(40).IsRequired();
        capability.Property(x => x.RequiredCredentialKind).HasConversion<string>().HasMaxLength(40).IsRequired();
        capability.Property(x => x.EndpointReference).HasMaxLength(300).IsRequired();
        capability.HasIndex(x => new { x.ProviderId, x.Capability }).IsUnique();

        var checkRun = modelBuilder.Entity<ProviderCheckRunRecord>();
        checkRun.ToTable("ProviderCheckRuns");
        checkRun.HasKey(x => x.Id);
        checkRun.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        checkRun.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        checkRun.Property(x => x.SafeErrorCode).HasMaxLength(120).IsRequired();
        checkRun.Property(x => x.SafeUserMessage).HasMaxLength(1000).IsRequired();
        checkRun.Property(x => x.CorrelationId).HasMaxLength(80).IsRequired();
        checkRun.HasIndex(x => new { x.ConnectionId, x.CompletedAtUtc });
        checkRun.HasOne<ProviderConnectionRecord>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        var observation = modelBuilder.Entity<ProviderObservationRecord>();
        observation.ToTable("ProviderObservations");
        observation.HasKey(x => x.Id);
        observation.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        observation.Property(x => x.Capability).HasConversion<string>().HasMaxLength(40).IsRequired();
        observation.Property(x => x.MetricName).HasMaxLength(160).IsRequired();
        observation.Property(x => x.Value).HasMaxLength(4000).IsRequired();
        observation.Property(x => x.Unit).HasMaxLength(60).IsRequired();
        observation.Property(x => x.Currency).HasMaxLength(12);
        observation.Property(x => x.Source).HasConversion<string>().HasMaxLength(80).IsRequired();
        observation.Property(x => x.Scope).HasConversion<string>().HasMaxLength(40).IsRequired();
        observation.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        observation.HasIndex(x => new { x.ConnectionId, x.Capability, x.MetricName, x.ObservedAtUtc });
        observation.HasIndex(x => new { x.CheckRunId, x.MetricName }).IsUnique();
        observation.HasOne<ProviderCheckRunRecord>()
            .WithMany()
            .HasForeignKey(x => x.CheckRunId)
            .OnDelete(DeleteBehavior.Cascade);
        observation.HasOne<ProviderConnectionRecord>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        var sync = modelBuilder.Entity<ProviderSyncStateRecord>();
        sync.ToTable("ProviderSyncStates");
        sync.HasKey(x => x.Id);
        sync.Property(x => x.ProviderId).HasMaxLength(100).IsRequired();
        sync.HasIndex(x => x.ConnectionId).IsUnique();
        sync.HasOne<ProviderConnectionRecord>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
