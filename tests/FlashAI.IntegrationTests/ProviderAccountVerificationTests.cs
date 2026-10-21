using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.IntegrationTests;

public sealed class ProviderAccountVerificationTests
{
    [Fact]
    public async Task MatchingVisibleAccountMenuIdentityIsVerified()
    {
        var request = Request("selected@example.test", new IdentitySession(
            new BrowserIdentityEvidence(true, true, "Selected@Example.Test", null, "Visible provider account-menu UI", null, DateTimeOffset.UtcNow)));

        var result = await new ProviderAccountVerificationService().VerifyAsync(request);

        Assert.Equal(IdentityVerificationStatus.IdentityVerified, result.Status);
        Assert.Equal("selected@example.test", result.ObservedProviderEmail);
        Assert.Null(result.SafeReasonCode);
    }

    [Fact]
    public async Task DifferentVisibleAccountMenuIdentityIsMismatchWithoutPromotion()
    {
        var request = Request("selected@example.test", new IdentitySession(
            new BrowserIdentityEvidence(true, true, "other@example.test", null, "Visible provider account-menu UI", null, DateTimeOffset.UtcNow)));

        var result = await new ProviderAccountVerificationService().VerifyAsync(request);

        Assert.Equal(IdentityVerificationStatus.Mismatch, result.Status);
        Assert.Equal("IDENTITY_EMAIL_MISMATCH", result.SafeReasonCode);
        Assert.Equal("other@example.test", result.ObservedProviderEmail);
    }

    [Fact]
    public async Task AuthenticatedShellWithoutAccountMenuRemainsUnverified()
    {
        var request = Request("selected@example.test", new IdentitySession(
            new BrowserIdentityEvidence(true, false, null, null, "Visible provider shell without account-menu evidence", "IDENTITY_ACCOUNT_MENU_UNAVAILABLE", DateTimeOffset.UtcNow)));

        var result = await new ProviderAccountVerificationService().VerifyAsync(request);

        Assert.Equal(IdentityVerificationStatus.Unverified, result.Status);
        Assert.Equal("IDENTITY_ACCOUNT_MENU_UNAVAILABLE", result.SafeReasonCode);
    }

    [Fact]
    public async Task SessionWithoutIdentityCapabilityRemainsUnverified()
    {
        var request = Request("selected@example.test", new BasicSession());

        var result = await new ProviderAccountVerificationService().VerifyAsync(request);

        Assert.Equal(IdentityVerificationStatus.Unverified, result.Status);
        Assert.Equal("IDENTITY_OBSERVATION_UNSUPPORTED", result.SafeReasonCode);
    }

    [Fact]
    public async Task DisplayNameOnlyEvidenceIsNotTreatedAsEmailIdentity()
    {
        var request = Request("selected@example.test", new IdentitySession(
            new BrowserIdentityEvidence(true, true, null, "Selected User", "Visible provider account-menu UI", "IDENTITY_EMAIL_UNAVAILABLE", DateTimeOffset.UtcNow)));

        var result = await new ProviderAccountVerificationService().VerifyAsync(request);

        Assert.Equal(IdentityVerificationStatus.Unverified, result.Status);
        Assert.Equal("IDENTITY_EMAIL_UNAVAILABLE", result.SafeReasonCode);
        Assert.Null(result.ObservedProviderEmail);
    }

    [Fact]
    public async Task EmailAliasesAreNotSilentlyCollapsed()
    {
        var request = Request("selected@example.test", new IdentitySession(
            new BrowserIdentityEvidence(true, true, "selected+alias@example.test", null, "Visible provider account-menu UI", null, DateTimeOffset.UtcNow)));

        var result = await new ProviderAccountVerificationService().VerifyAsync(request);

        Assert.Equal(IdentityVerificationStatus.Mismatch, result.Status);
        Assert.Equal("IDENTITY_EMAIL_MISMATCH", result.SafeReasonCode);
    }

    [Fact]
    public void NonObservedEmailValueCannotVerifyIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var observation = new ProviderAccountObservation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "openai-chatgpt",
            "managed-playwright-context",
            now,
            "en",
            true,
            true,
            "PartiallyResolved",
            "Visible provider account-menu and official Settings UI",
            "ACCOUNT_SETTINGS_UNAVAILABLE",
            new(ProviderAccountObservationField.DisplayName, "Selected User", "OfficialAccountUI", "VisibleLabel", "High", ProviderAccountObservationFieldStatus.Observed, now),
            new(ProviderAccountObservationField.Email, "selected@example.test", "CachedLocalValue", "Unknown", "Low", ProviderAccountObservationFieldStatus.Unavailable, now, "IDENTITY_EMAIL_NOT_EXPOSED"),
            new(ProviderAccountObservationField.Plan, null, "OfficialAccountOrPlanUI", "OfficialAccountUI", "Low", ProviderAccountObservationFieldStatus.Unavailable, now, "PLAN_LABEL_UNAVAILABLE"),
            new(ProviderAccountObservationField.MfaConfiguration, null, "OfficialSecuritySettings", "OfficialSecurityUI", "Low", ProviderAccountObservationFieldStatus.Unavailable, now, "MFA_CONFIGURATION_UNAVAILABLE"),
            new(ProviderAccountObservationField.UsageQuota, null, "OfficialAccountOrPlanUI", "OfficialAccountUI", "Low", ProviderAccountObservationFieldStatus.Unsupported, now, "UNSUPPORTED_USAGE_SOURCE"));

        var result = new ProviderAccountVerificationService().VerifyObservation(
            new ProviderIdentityVerificationRequest(
                observation.AccountId,
                observation.AttemptId,
                observation.BrowserProfileBindingId,
                observation.ProviderId,
                "selected@example.test",
                new BasicSession()),
            observation);

        Assert.Equal(IdentityVerificationStatus.Unverified, result.Status);
        Assert.Equal("IDENTITY_EMAIL_NOT_EXPOSED", result.SafeReasonCode);
        Assert.Null(result.ObservedProviderEmail);
    }

    [Fact]
    public void MatchingOfficialAccountSettingsEmailVerifiesIdentityWithoutAccountMenu()
    {
        var now = DateTimeOffset.UtcNow;
        var accountId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var observation = new ProviderAccountObservation(
            accountId,
            attemptId,
            bindingId,
            "openai-chatgpt",
            "managed-context",
            now,
            "vi",
            true,
            false,
            "Observed",
            "official ChatGPT account settings UI",
            null,
            new(ProviderAccountObservationField.DisplayName, null, "OfficialAccountUI", "OfficialAccountUI", "Low", ProviderAccountObservationFieldStatus.Unavailable, now),
            new(ProviderAccountObservationField.Email, "selected@example.test", "OfficialSettingsAccount", "OfficialSettingsAccount", "High", ProviderAccountObservationFieldStatus.Observed, now),
            new(ProviderAccountObservationField.Plan, "Free", "OfficialAccountUI", "OfficialAccountOrPlanUI", "High", ProviderAccountObservationFieldStatus.Observed, now),
            new(ProviderAccountObservationField.MfaConfiguration, null, "OfficialSecuritySettings", "OfficialSecurityUI", "Low", ProviderAccountObservationFieldStatus.Unavailable, now),
            new(ProviderAccountObservationField.UsageQuota, null, "OfficialAccountUI", "OfficialAccountOrPlanUI", "Low", ProviderAccountObservationFieldStatus.Unsupported, now));

        var result = new ProviderAccountVerificationService().VerifyObservation(
            new ProviderIdentityVerificationRequest(
                accountId,
                attemptId,
                bindingId,
                "openai-chatgpt",
                "selected@example.test",
                new BasicSession()),
            observation);

        Assert.Equal(IdentityVerificationStatus.IdentityVerified, result.Status);
        Assert.Equal("selected@example.test", result.ObservedProviderEmail);
        Assert.Null(result.SafeReasonCode);
    }

    [Fact]
    public void ObservationFromAnotherAccountContextIsRejectedBeforeIdentityComparison()
    {
        var request = new ProviderIdentityVerificationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "openai-chatgpt",
            "selected@example.test",
            new BasicSession());
        var observation = new ProviderAccountObservation(
            Guid.NewGuid(),
            request.AttemptId,
            request.BrowserProfileBindingId,
            request.ProviderId,
            "managed-context",
            DateTimeOffset.UtcNow,
            "en",
            true,
            true,
            "Observed",
            "Visible provider account-menu UI",
            null,
            new(ProviderAccountObservationField.DisplayName, "Other User", "OfficialAccountUI", "VisibleLabel", "High", ProviderAccountObservationFieldStatus.Observed, DateTimeOffset.UtcNow),
            new(ProviderAccountObservationField.Email, "other@example.test", "OfficialAccountUI", "VisibleLabel", "High", ProviderAccountObservationFieldStatus.Observed, DateTimeOffset.UtcNow),
            new(ProviderAccountObservationField.Plan, "Plus", "OfficialAccountUI", "VisibleLabel", "High", ProviderAccountObservationFieldStatus.Observed, DateTimeOffset.UtcNow),
            new(ProviderAccountObservationField.MfaConfiguration, null, "OfficialSecuritySettings", "VisibleLabel", "Low", ProviderAccountObservationFieldStatus.Unavailable, DateTimeOffset.UtcNow),
            new(ProviderAccountObservationField.UsageQuota, null, "OfficialAccountUI", "VisibleLabel", "Low", ProviderAccountObservationFieldStatus.Unsupported, DateTimeOffset.UtcNow, "UNSUPPORTED_USAGE_SOURCE"));

        var result = new ProviderAccountVerificationService().VerifyObservation(request, observation);

        Assert.Equal(IdentityVerificationStatus.Unverified, result.Status);
        Assert.Equal("IDENTITY_OBSERVATION_SCOPE_MISMATCH", result.SafeReasonCode);
        Assert.Null(result.AccountObservation);
        Assert.Null(result.ObservedProviderEmail);
    }

    private static ProviderIdentityVerificationRequest Request(
        string intendedEmail,
        IBrowserAuthenticationSession session)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "openai-chatgpt", intendedEmail, session);

    private sealed class IdentitySession(BrowserIdentityEvidence evidence) : BasicSession, IBrowserIdentityObservationSession
    {
        public Task<BrowserIdentityEvidence> ReadIdentityEvidenceAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(evidence);
    }

    private class BasicSession : IBrowserAuthenticationSession
    {
        public Uri CurrentUri => new("https://chatgpt.com/");

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserPageSnapshot(CurrentUri, BrowserPageState.Authenticated));

        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
