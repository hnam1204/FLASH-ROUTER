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
