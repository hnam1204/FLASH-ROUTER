using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Persistence;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class ProviderAccountObservationTests
{
    [Fact]
    public async Task MatchingEmailUsesReadOnlyObservationWithoutAuthenticationReplay()
    {
        var accountId = Guid.NewGuid();
        var session = new RichObservationSession(Observation(accountId, "selected@example.test"));
        var request = Request(accountId, "selected@example.test", session);

        var result = await new ProviderAccountVerificationService().VerifyAsync(request);

        Assert.Equal(IdentityVerificationStatus.IdentityVerified, result.Status);
        Assert.Equal("Plus", result.AccountObservation?.Plan.Value);
        Assert.Equal("Enabled", result.AccountObservation?.MfaConfiguration.Value);
        Assert.Equal(1, session.ObservationReadCount);
        Assert.Equal(0, session.AuthenticationActionCount);
    }

    [Fact]
    public async Task DifferentEmailNeverOverwritesStoredIdentityAndBlocksVerification()
    {
        var accountId = Guid.NewGuid();
        var session = new RichObservationSession(Observation(accountId, "other@example.test"));

        var result = await new ProviderAccountVerificationService().VerifyAsync(
            Request(accountId, "selected@example.test", session));

        Assert.Equal(IdentityVerificationStatus.Mismatch, result.Status);
        Assert.Equal("IDENTITY_EMAIL_MISMATCH", result.SafeReasonCode);
        Assert.Equal("other@example.test", result.ObservedProviderEmail);
    }

    [Fact]
    public async Task DisplayNameOnlyAndUnavailableSettingsRemainPartialEvidence()
    {
        var accountId = Guid.NewGuid();
        var observation = Observation(
            accountId,
            email: null,
            displayName: "Selected User",
            plan: null,
            mfa: null,
            status: "PartiallyResolved",
            failureReasonCode: "ACCOUNT_SETTINGS_UNAVAILABLE");
        var store = new InMemoryProviderAccountObservationStore();
        await store.SaveAsync(observation);

        var latest = await store.GetLatestAsync(accountId);

        Assert.NotNull(latest);
        Assert.Equal("Selected User", Field(latest!, ProviderAccountObservationField.DisplayName).Value);
        Assert.Equal(ProviderAccountObservationFieldStatus.Unavailable, Field(latest, ProviderAccountObservationField.Email).Status);
        Assert.Equal("ACCOUNT_SETTINGS_UNAVAILABLE", latest.FailureReasonCode);
        Assert.Equal(ProviderAccountObservationFieldStatus.Unsupported, Field(latest, ProviderAccountObservationField.UsageQuota).Status);
    }

    [Theory]
    [InlineData("Free")]
    [InlineData("Plus")]
    [InlineData("Pro")]
    [InlineData("Unknown")]
    public async Task PlanLabelsAndLocaleAreStoredAsObservedProviderValues(string plan)
    {
        var accountId = Guid.NewGuid();
        var observation = Observation(accountId, "locale@example.test", plan: plan, locale: "vi");
        var store = new InMemoryProviderAccountObservationStore();
        await store.SaveAsync(observation);

        var latest = await store.GetLatestAsync(accountId);

        Assert.Equal("vi", latest?.Locale);
        Assert.Equal(plan, Field(latest!, ProviderAccountObservationField.Plan).Value);
    }

    [Fact]
    public async Task ConcurrentAccountsRemainIsolated()
    {
        var store = new InMemoryProviderAccountObservationStore();
        var first = Observation(Guid.NewGuid(), "first@example.test", plan: "Free");
        var second = Observation(Guid.NewGuid(), "second@example.test", plan: "Pro");

        await Task.WhenAll(store.SaveAsync(first), store.SaveAsync(second));

        Assert.Equal("first@example.test", Field((await store.GetLatestAsync(first.AccountId))!, ProviderAccountObservationField.Email).Value);
        Assert.Equal("second@example.test", Field((await store.GetLatestAsync(second.AccountId))!, ProviderAccountObservationField.Email).Value);
    }

    [Fact]
    public async Task ClosedBrowserIsSafeFailureAndDoesNotBecomeMismatch()
    {
        var session = new ClosedObservationSession();
        var result = await new ProviderAccountVerificationService().VerifyAsync(
            Request(Guid.NewGuid(), "selected@example.test", session));

        Assert.Equal(IdentityVerificationStatus.Unverified, result.Status);
        Assert.Equal("BROWSER_CLOSED", result.SafeReasonCode);
        Assert.NotEqual(IdentityVerificationStatus.Mismatch, result.Status);
    }

    private static ProviderIdentityVerificationRequest Request(
        Guid accountId,
        string intendedEmail,
        IBrowserAuthenticationSession session)
        => new(accountId, Guid.NewGuid(), Guid.NewGuid(), "openai-chatgpt", intendedEmail, session);

    private static ProviderAccountObservation Observation(
        Guid accountId,
        string? email,
        string? displayName = "Selected User",
        string? plan = "Plus",
        string? mfa = "Enabled",
        string locale = "en",
        string status = "Observed",
        string? failureReasonCode = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            accountId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "openai-chatgpt",
            "managed-playwright-context",
            now,
            locale,
            true,
            true,
            status,
            "Visible provider account-menu and official Settings UI",
            failureReasonCode,
            Field(ProviderAccountObservationField.DisplayName, displayName, now),
            Field(ProviderAccountObservationField.Email, email, now, email is null ? "IDENTITY_EMAIL_NOT_EXPOSED" : null),
            Field(ProviderAccountObservationField.Plan, plan, now, plan is null ? "PLAN_LABEL_UNAVAILABLE" : null),
            Field(ProviderAccountObservationField.MfaConfiguration, mfa, now, mfa is null ? "MFA_CONFIGURATION_UNAVAILABLE" : null),
            new(ProviderAccountObservationField.UsageQuota, null, "OfficialAccountOrPlanUI", "OfficialAccountUI", "Low", ProviderAccountObservationFieldStatus.Unsupported, now, "UNSUPPORTED_USAGE_SOURCE"));
    }

    private static ProviderAccountFieldEvidence Field(
        ProviderAccountObservationField field,
        string? value,
        DateTimeOffset observedAtUtc,
        string? failureReasonCode = null)
        => new(
            field,
            value,
            "Official account/settings UI",
            "OfficialAccountUI",
            value is null ? "Low" : "High",
            value is null ? ProviderAccountObservationFieldStatus.Unavailable : ProviderAccountObservationFieldStatus.Observed,
            observedAtUtc,
            failureReasonCode);

    private static ProviderAccountFieldEvidence Field(
        ProviderAccountObservationSnapshot snapshot,
        ProviderAccountObservationField field)
        => snapshot.Fields.Single(item => item.Field == field);

    private sealed class RichObservationSession(ProviderAccountObservation observation)
        : IBrowserAuthenticationSession, IBrowserAccountObservationSession
    {
        public int ObservationReadCount { get; private set; }
        public int AuthenticationActionCount { get; private set; }
        public Uri CurrentUri => new("https://chatgpt.com/");

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => ActionAsync();

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserPageSnapshot(CurrentUri, BrowserPageState.Authenticated));

        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => ActionAsync();

        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default) => ActionAsync();

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default) => ActionAsync();

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default) => ActionAsync();

        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default) => ActionAsync();

        public Task<ProviderAccountObservation> ReadAccountObservationAsync(
            Guid accountId,
            Guid attemptId,
            Guid browserProfileBindingId,
            CancellationToken cancellationToken = default)
        {
            ObservationReadCount++;
            return Task.FromResult(observation with
            {
                AccountId = accountId,
                AttemptId = attemptId,
                BrowserProfileBindingId = browserProfileBindingId
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Task ActionAsync()
        {
            AuthenticationActionCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ClosedObservationSession : RichObservationSessionBase, IBrowserAccountObservationSession
    {
        public Task<ProviderAccountObservation> ReadAccountObservationAsync(
            Guid accountId,
            Guid attemptId,
            Guid browserProfileBindingId,
            CancellationToken cancellationToken = default)
            => throw new BrowserAuthSessionException("BROWSER_CLOSED", "Browser context đã đóng.");
    }

    private abstract class RichObservationSessionBase : IBrowserAuthenticationSession
    {
        public Uri CurrentUri => new("https://chatgpt.com/");
        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BrowserPageSnapshot(CurrentUri, BrowserPageState.Authenticated));
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class SqliteProviderAccountObservationTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ProvenancePersistsAndProjectsWithoutChangingAccountEmail()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var created = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = "stored@example.test",
            DisplayName = "Stored",
            Provider = "openai-chatgpt",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(created.IsSuccess, created.Message);

        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        var accountId = (await reader.GetAccountsAsync("stored@example.test")).Single().AccountId;
        var store = new SqliteProviderAccountObservationStore(_fixture.ContextFactory);
        var observation = new ProviderAccountTestsObservationFactory().Create(accountId, "observed@example.test");

        await store.SaveAsync(observation);
        var projected = await store.GetLatestAsync(accountId);
        var detail = await reader.GetAccountDetailAsync(accountId);

        Assert.NotNull(projected);
        Assert.Equal("observed@example.test", projected!.Fields.Single(item => item.Field == ProviderAccountObservationField.Email).Value);
        Assert.Equal(ProviderAccountObservationFieldStatus.Unsupported, projected.Fields.Single(item => item.Field == ProviderAccountObservationField.UsageQuota).Status);
        Assert.Equal("stored@example.test", detail!.Email);
    }
}

internal sealed class ProviderAccountTestsObservationFactory
{
    public ProviderAccountObservation Create(Guid accountId, string email)
    {
        var now = DateTimeOffset.UtcNow;
        static ProviderAccountFieldEvidence Field(ProviderAccountObservationField field, string? value, DateTimeOffset observedAtUtc, ProviderAccountObservationFieldStatus status, string? reason = null)
            => new(field, value, "OfficialSettingsAccount", "VisibleLabel", status == ProviderAccountObservationFieldStatus.Observed ? "High" : "Low", status, observedAtUtc, reason);

        return new(
            accountId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "openai-chatgpt",
            "managed-playwright-context",
            now,
            "en",
            true,
            true,
            "Observed",
            "Visible provider account-menu and official Settings UI",
            null,
            Field(ProviderAccountObservationField.DisplayName, "Observed User", now, ProviderAccountObservationFieldStatus.Observed),
            Field(ProviderAccountObservationField.Email, email, now, ProviderAccountObservationFieldStatus.Observed),
            Field(ProviderAccountObservationField.Plan, "Pro", now, ProviderAccountObservationFieldStatus.Observed),
            Field(ProviderAccountObservationField.MfaConfiguration, "Enabled", now, ProviderAccountObservationFieldStatus.Observed),
            Field(ProviderAccountObservationField.UsageQuota, null, now, ProviderAccountObservationFieldStatus.Unsupported, "UNSUPPORTED_USAGE_SOURCE"));
    }
}
