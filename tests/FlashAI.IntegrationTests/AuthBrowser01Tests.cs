using System.Collections.Concurrent;
using System.Security.Cryptography;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class AuthBrowser01Tests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();
    private readonly string _vaultRoot = Path.Combine(Path.GetTempPath(), "flash-router-auth-browser-" + Guid.NewGuid().ToString("N"));
    private IDataProtectionProvider _dataProtection = null!;
    private ISecretVault _vault = null!;
    private VaultAccessState _vaultAccess = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _dataProtection = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_vaultRoot, "keys")),
            builder => builder.SetApplicationName("FLASH ROUTER AUTH BROWSER TEST"));
        _vault = new FileSecretVault(Path.Combine(_vaultRoot, "vault"), _dataProtection);
        _vaultAccess = new VaultAccessState();
    }

    public async Task DisposeAsync()
    {
        (_vault as IDisposable)?.Dispose();
        (_dataProtection as IDisposable)?.Dispose();
        await _database.DisposeAsync();
        if (Directory.Exists(_vaultRoot))
        {
            Directory.Delete(_vaultRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SyntheticFlowRequiresConfirmationAndUsesLocalTotp()
    {
        var accountId = await CreateAccountAsync("synthetic-auth@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-auth@example.test", "SyntheticPassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);

        var session = new SyntheticBrowserSession();
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.EmailRequired, started.State);
        Assert.Equal(BrowserAuthAction.FillEmail, started.NextAction);

        var unconfirmed = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: false);
        Assert.Equal("CONFIRMATION_REQUIRED", unconfirmed.Status);
        Assert.Empty(session.FilledValues);

        var email = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillEmail, confirmed: true);
        Assert.Equal(BrowserAuthState.PasswordRequired, email.State);
        Assert.Equal("synthetic-auth@example.test", session.FilledValues[BrowserAuthField.Email]);

        var password = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, confirmed: true);
        Assert.Equal(BrowserAuthState.MfaRequired, password.State);
        Assert.Equal("SyntheticPassword-123", session.FilledValues[BrowserAuthField.Password]);

        var mfa = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, confirmed: true);
        Assert.Equal(BrowserAuthState.BrowserSessionObserved, mfa.State);
        Assert.False(mfa.IsSuccess);
        Assert.Equal("BrowserSessionObserved", mfa.Observation is null ? null : "BrowserSessionObserved");
        Assert.Equal(6, session.FilledValues[BrowserAuthField.Totp].Length);
        Assert.Equal("IDENTITY_UNCONFIRMED", mfa.Status);
        Assert.Equal(IdentityVerificationStatus.Unverified, mfa.ObservationResult?.IdentityStatus);
        Assert.NotNull(mfa.Observation);
    }

    [Fact]
    public async Task AutoLoginRunsOneScopedWorkflowAcrossEmailPasswordAndTotp()
    {
        var accountId = await CreateAccountAsync("synthetic-auto@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-auto@example.test", "SyntheticAutoPassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);
        var session = new SyntheticBrowserSession();
        var stages = new ConcurrentQueue<string>();
        await using var orchestrator = CreateOrchestrator(session);

        var result = await orchestrator.AutoLoginAsync(
            accountId,
            credentialRotationAttested: true,
            progress: new Progress<BrowserAuthProgress>(item => stages.Enqueue(item.Stage)));

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.False(result.IsSuccess);
        Assert.Equal("IDENTITY_UNCONFIRMED", result.Status);
        Assert.Equal("synthetic-auto@example.test", session.FilledValues[BrowserAuthField.Email]);
        Assert.Equal("SyntheticAutoPassword-123", session.FilledValues[BrowserAuthField.Password]);
        Assert.Equal(6, session.FilledValues[BrowserAuthField.Totp].Length);
        Assert.Contains("OpeningBrowser", stages);
        Assert.Contains("FillingEmail", stages);
        Assert.Contains("FillingPassword", stages);
        Assert.Contains("FillingTotp", stages);
        Assert.Contains("CheckingAuthenticatedSession", stages);
    }

    [Fact]
    public async Task TotpProgressAdvancesToPostLoginObservationAndEmitsTerminalEvent()
    {
        var accountId = await CreateAccountAsync("synthetic-progress@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-progress@example.test", "SyntheticProgressPassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);

        var progress = new List<BrowserAuthProgress>();
        await using var orchestrator = CreateOrchestrator(new SyntheticBrowserSession());
        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true, new InlineProgress(progress));
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true, new InlineProgress(progress));
        var result = await orchestrator.ConfirmAndFillAsync(
            started.AttemptId.Value,
            BrowserAuthAction.FillTotp,
            true,
            new InlineProgress(progress));

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.Contains(progress, item => item.Stage == "SubmittingTotp");
        Assert.Contains(progress, item => item.Stage == "ObservingPostLogin");
        Assert.Contains(progress, item => item.State == BrowserAuthState.BrowserSessionObserved && item.IsTerminal);
        Assert.DoesNotContain(progress, item => item.State == BrowserAuthState.TimedOut);
    }

    [Fact]
    public async Task PostTotpUnchangedMfaReturnsManualActionWithoutFalseSuccessOrResubmission()
    {
        var accountId = await CreateAccountAsync("synthetic-additional-mfa@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-additional-mfa@example.test", "SyntheticMfaPassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var session = new PostTotpMfaSession(MfaObservationKind.MfaFormUnchanged);
        var registration = new TrackingSessionRegistration();
        await using var orchestrator = CreateOrchestrator(session, sessionRegistration: registration);
        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);

        var progress = new List<BrowserAuthProgress>();
        var waiting = await orchestrator.ConfirmAndFillAsync(
            started.AttemptId.Value,
            BrowserAuthAction.FillTotp,
            true,
            new InlineProgress(progress));

        Assert.False(waiting.IsSuccess);
        Assert.Equal(BrowserAuthState.WaitingForUser, waiting.State);
        Assert.Equal("ADDITIONAL_MFA_REQUIRED", waiting.Status);
        Assert.Null(waiting.NextAction);
        Assert.Equal(MfaObservationKind.MfaFormUnchanged, waiting.MfaObservation);
        Assert.Equal(1, session.TotpSubmissionCount);
        Assert.False(session.IsDisposed);
        Assert.DoesNotContain(started.AttemptId.Value, registration.UnregisteredAttemptIds);

        session.ResolveToAuthenticated();
        var confirmed = await orchestrator.RefreshAsync(started.AttemptId.Value);

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, confirmed.State);
        Assert.False(confirmed.IsSuccess);
        Assert.Equal("IDENTITY_UNCONFIRMED", confirmed.Status);
        Assert.Contains(progress, item => item.State == BrowserAuthState.WaitingForUser && item.IsTerminal);
        Assert.Equal(1, session.TotpSubmissionCount);
        await orchestrator.CancelAsync(started.AttemptId.Value);
    }

    [Fact]
    public async Task PostTotpValidationErrorIsClassifiedWithoutRetryingTotp()
    {
        var accountId = await CreateAccountAsync("synthetic-mfa-validation@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-mfa-validation@example.test", "SyntheticMfaValidationPassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var session = new PostTotpMfaSession(MfaObservationKind.MfaValidationError);
        await using var orchestrator = CreateOrchestrator(session);
        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);

        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, true);

        Assert.False(result.IsSuccess);
        Assert.Equal(BrowserAuthState.WaitingForUser, result.State);
        Assert.Equal("ADDITIONAL_MFA_REQUIRED", result.Status);
        Assert.Equal(MfaObservationKind.MfaValidationError, result.MfaObservation);
        Assert.Equal(1, session.TotpSubmissionCount);
        await orchestrator.CancelAsync(started.AttemptId.Value);
    }

    [Fact]
    public async Task BrowserClosesAfterSixInvalidPasswordAttemptsForTheSameAccount()
    {
        const string email = "synthetic-invalid-retry@example.test";
        var accountId = await CreateAccountAsync(email);
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(email, "SyntheticInvalidRetryPassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var factory = new InvalidCredentialSessionFactory();
        await using var orchestrator = CreateOrchestrator(new InvalidCredentialSession(), factory);

        for (var attemptNumber = 1; attemptNumber <= 6; attemptNumber++)
        {
            var started = await orchestrator.StartAsync(accountId);
            Assert.Equal(BrowserAuthState.EmailRequired, started.State);
            await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);

            var result = await orchestrator.ConfirmAndFillAsync(
                started.AttemptId.Value,
                BrowserAuthAction.FillPassword,
                true);
            var session = factory.Sessions[attemptNumber - 1];

            Assert.Equal(BrowserAuthState.HumanActionRequired, result.State);
            if (attemptNumber <= 5)
            {
                Assert.Equal("INVALID_CREDENTIALS", result.Status);
                Assert.False(session.IsDisposed);
            }
            else
            {
                Assert.Equal("INVALID_CREDENTIALS_BROWSER_CLOSED", result.Status);
                Assert.Contains("6 lần", result.Message, StringComparison.Ordinal);
                Assert.True(session.IsDisposed);
            }

            await orchestrator.CancelAsync(started.AttemptId.Value);
        }
    }

    [Fact]
    public async Task SlowPasswordSubmissionWaitsForProviderAndAcceptsSuccessfulTransition()
    {
        const string email = "slow-provider-response@example.test";
        var accountId = await CreateAccountAsync(email);
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(email, "SlowProviderPassword-123", null, false))).IsSuccess);

        var session = new DelayedCredentialTransitionSession(readsBeforeTransition: 2);
        await using var orchestrator = CreateOrchestrator(
            session,
            credentialTransitionBudget: TimeSpan.FromSeconds(2));

        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = await orchestrator.ConfirmAndFillAsync(
            started.AttemptId.Value,
            BrowserAuthAction.FillPassword,
            true);

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.True(timer.Elapsed >= TimeSpan.FromMilliseconds(300), $"Expected polling to wait for the delayed provider response; elapsed {timer.Elapsed}.");
        Assert.Equal(1, session.PasswordSubmissionCount);
        Assert.False(session.IsDisposed);
    }

    [Fact]
    public async Task SlowPasswordSubmissionClosesBrowserAfterResponseBudgetWithoutResubmitting()
    {
        const string email = "unresponsive-provider@example.test";
        var accountId = await CreateAccountAsync(email);
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(email, "UnresponsiveProviderPassword-123", null, false))).IsSuccess);

        var session = new DelayedCredentialTransitionSession(readsBeforeTransition: int.MaxValue);
        await using var orchestrator = CreateOrchestrator(
            session,
            credentialTransitionBudget: TimeSpan.FromMilliseconds(350));

        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = await orchestrator.ConfirmAndFillAsync(
            started.AttemptId.Value,
            BrowserAuthAction.FillPassword,
            true);

        Assert.Equal(BrowserAuthState.TimedOut, result.State);
        Assert.Equal("CREDENTIAL_TRANSITION_TIMEOUT", result.Status);
        Assert.Contains("luồng đăng nhập bình thường", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(timer.Elapsed >= TimeSpan.FromMilliseconds(350), $"Expected the response budget to elapse; elapsed {timer.Elapsed}.");
        Assert.Equal(1, session.PasswordSubmissionCount);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task ManualMfaLeaseExpiresAndCleansUpRetainedSession()
    {
        var accountId = await CreateAccountAsync("synthetic-mfa-lease@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-mfa-lease@example.test", "SyntheticMfaLeasePassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var session = new PostTotpMfaSession(MfaObservationKind.AdditionalMfaChallenge);
        var registration = new TrackingSessionRegistration();
        await using var orchestrator = CreateOrchestrator(
            session,
            sessionRegistration: registration,
            attemptLifetime: TimeSpan.FromMilliseconds(180));
        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);
        var waiting = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, true);

        Assert.Equal(BrowserAuthState.WaitingForUser, waiting.State);
        await Task.Delay(350);

        Assert.True(session.IsDisposed);
        Assert.Contains(started.AttemptId.Value, registration.UnregisteredAttemptIds);
        Assert.Equal("ATTEMPT_NOT_FOUND", (await orchestrator.RefreshAsync(started.AttemptId.Value)).Status);
    }

    [Fact]
    public async Task AdditionalMfaCompletionAuditIsWarningNotSuccess()
    {
        var accountId = await CreateAccountAsync("synthetic-mfa-audit@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-mfa-audit@example.test", "SyntheticMfaAuditPassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var audit = new CapturingAuditSink();
        await using var orchestrator = CreateOrchestrator(
            new PostTotpMfaSession(MfaObservationKind.MfaFormUnchanged),
            auditSink: audit);
        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, true);

        await Task.Delay(50);
        var completed = audit.Events.Single(item => item.EventType == "AuthenticationAttemptCompleted");
        Assert.Equal("ADDITIONAL_MFA_REQUIRED", completed.ErrorCode);
        Assert.Equal("Warning", completed.Severity);
        Assert.DoesNotContain(audit.Events, item => item.EventType == "AutoLoginCompleted");
        Assert.False(result.IsSuccess);
        await orchestrator.CancelAsync(started.AttemptId.Value);
    }

    [Fact]
    public async Task StalledPostLoginObservationClosesBrowserAtDeadlineWithoutFalseSuccess()
    {
        var accountId = await CreateAccountAsync("synthetic-stalled@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-stalled@example.test", "SyntheticStalledPassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);

        var progress = new List<BrowserAuthProgress>();
        var session = new StalledPostLoginSession();
        await using var orchestrator = CreateOrchestrator(
            session,
            postLoginObservationBudget: TimeSpan.FromMilliseconds(180),
            postLoginOperationBudget: TimeSpan.FromMilliseconds(25));
        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await orchestrator.ConfirmAndFillAsync(
            started.AttemptId.Value,
            BrowserAuthAction.FillTotp,
            true,
            new InlineProgress(progress));
        stopwatch.Stop();

        Assert.Equal(BrowserAuthState.TimedOut, result.State);
        Assert.Equal("POST_LOGIN_OBSERVATION_TIMEOUT", result.Status);
        Assert.True(session.IsDisposed);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.Contains(progress, item => item.State == BrowserAuthState.ObservingPostLogin);
        Assert.Contains(progress, item => item.State == BrowserAuthState.TimedOut);
        Assert.DoesNotContain(progress, item => item.State == BrowserAuthState.BrowserSessionObserved);
        Assert.True(await AccountExistsAsync(accountId), "A timeout must not delete or recreate the selected account.");
        Assert.Equal("ATTEMPT_NOT_FOUND", (await orchestrator.RefreshAsync(started.AttemptId.Value)).Status);
    }

    [Fact]
    public async Task SlowPostLoginSucceedsWhenAuthenticatedShellAppearsWithinObservationBudget()
    {
        var accountId = await CreateAccountAsync("synthetic-pending-recovery@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-pending-recovery@example.test", "SyntheticPendingPassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var session = new StalledPostLoginSession();
        var registration = new TrackingSessionRegistration();
        await using var orchestrator = CreateOrchestrator(
            session,
            postLoginObservationBudget: TimeSpan.FromSeconds(1),
            postLoginOperationBudget: TimeSpan.FromMilliseconds(40),
            sessionRegistration: registration);

        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            session.ReleaseToAuthenticated();
        });
        var observed = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, true);
        await releaseTask;

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, observed.State);
        Assert.Equal(1, session.TotpSubmissionCount);
        Assert.False(session.IsDisposed);
        Assert.DoesNotContain(started.AttemptId.Value, registration.UnregisteredAttemptIds);
        await orchestrator.CancelAsync(started.AttemptId.Value);
        Assert.Contains(started.AttemptId.Value, registration.UnregisteredAttemptIds);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task PostLoginObservationTimeoutClosesAndCleansUpContext()
    {
        var accountId = await CreateAccountAsync("synthetic-pending-expiry@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-pending-expiry@example.test", "SyntheticPendingExpiryPassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var session = new StalledPostLoginSession();
        var registration = new TrackingSessionRegistration();
        await using var orchestrator = CreateOrchestrator(
            session,
            postLoginObservationBudget: TimeSpan.FromMilliseconds(80),
            postLoginOperationBudget: TimeSpan.FromMilliseconds(15),
            sessionRegistration: registration,
            attemptLifetime: TimeSpan.FromMilliseconds(180));

        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);
        var timedOut = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, true);
        Assert.Equal(BrowserAuthState.TimedOut, timedOut.State);
        Assert.Equal("POST_LOGIN_OBSERVATION_TIMEOUT", timedOut.Status);

        Assert.True(session.IsDisposed);
        Assert.Contains(started.AttemptId.Value, registration.UnregisteredAttemptIds);
        var expired = await orchestrator.RefreshAsync(started.AttemptId.Value);
        Assert.Equal("ATTEMPT_NOT_FOUND", expired.Status);
    }

    [Fact]
    public async Task BrowserContextClosingDuringPostLoginObservationCleansUpAttempt()
    {
        var accountId = await CreateAccountAsync("synthetic-pending-closed@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-pending-closed@example.test", "SyntheticPendingClosedPassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var session = new StalledPostLoginSession();
        var registration = new TrackingSessionRegistration();
        await using var orchestrator = CreateOrchestrator(
            session,
            postLoginObservationBudget: TimeSpan.FromMilliseconds(80),
            postLoginOperationBudget: TimeSpan.FromMilliseconds(15),
            sessionRegistration: registration);

        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);
        session.ThrowOnNextRead = true;
        var closed = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, true);

        Assert.Equal(BrowserAuthState.Failed, closed.State);
        Assert.Equal("BROWSER_CLOSED", closed.Status);
        Assert.True(session.IsDisposed);
        Assert.Contains(started.AttemptId.Value, registration.UnregisteredAttemptIds);
        Assert.Equal("ATTEMPT_NOT_FOUND", (await orchestrator.RefreshAsync(started.AttemptId.Value)).Status);
    }

    [Fact]
    public async Task CancellationDuringPostLoginObservationStopsTheReadAndCleansUp()
    {
        var accountId = await CreateAccountAsync("synthetic-cancel-observation@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        Assert.True((await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-cancel-observation@example.test", "SyntheticCancelPassword-123", "JBSWY3DPEHPK3PXP", false))).IsSuccess);

        var session = new StalledPostLoginSession();
        await using var orchestrator = CreateOrchestrator(
            session,
            postLoginObservationBudget: TimeSpan.FromSeconds(5),
            postLoginOperationBudget: TimeSpan.FromSeconds(5));

        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);

        var observation = orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, true);
        await session.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var cancelled = await orchestrator.CancelAsync(started.AttemptId.Value);
        var observedResult = await observation;

        Assert.Equal(BrowserAuthState.Cancelled, cancelled.State);
        Assert.Equal(BrowserAuthState.Cancelled, observedResult.State);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task PostLoginObservationRetriesTransientDomTimeoutWithoutResubmittingTotp()
    {
        var session = new TransientPostLoginObservationSession("BROWSER_DOM_READ_TIMEOUT", 2);
        var result = await RunTransientPostLoginScenarioAsync(session, "synthetic-dom-retry@example.test");

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.Equal("IDENTITY_UNCONFIRMED", result.Status);
        Assert.Equal(1, session.FillCounts[BrowserAuthField.Totp]);
        Assert.Equal(2, session.TransientObservationCount);
    }

    [Fact]
    public async Task PostLoginObservationRetriesDelayedNavigationWithoutResubmittingCredentials()
    {
        var session = new TransientPostLoginObservationSession("BROWSER_NAVIGATION_IN_PROGRESS", 2);
        var result = await RunTransientPostLoginScenarioAsync(session, "synthetic-navigation-retry@example.test");

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.Equal(1, session.FillCounts[BrowserAuthField.Email]);
        Assert.Equal(1, session.FillCounts[BrowserAuthField.Password]);
        Assert.Equal(1, session.FillCounts[BrowserAuthField.Totp]);
    }

    private async Task<BrowserAuthResult> RunTransientPostLoginScenarioAsync(
        TransientPostLoginObservationSession session,
        string email)
    {
        var accountId = await CreateAccountAsync(email);
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(email, "SyntheticTransientPassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);

        await using var orchestrator = CreateOrchestrator(
            session,
            postLoginObservationBudget: TimeSpan.FromSeconds(2),
            postLoginOperationBudget: TimeSpan.FromMilliseconds(100));
        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, true);
        return await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, true);
    }

    [Fact]
    public async Task CleanReloginUsesFreshAccountBoundCandidateAndPreservesOriginalBinding()
    {
        var accountId = await CreateAccountAsync("synthetic-clean-relogin@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(
                "synthetic-clean-relogin@example.test",
                "SyntheticCleanReloginPassword-123",
                "JBSWY3DPEHPK3PXP",
                false));
        Assert.True(save.IsSuccess, save.Message);

        var originalBinding = new BrowserProfileBindingVm(
            Guid.NewGuid(),
            accountId,
            "openai-chatgpt",
            "Chrome",
            "original-profile",
            "Ready",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var profileManager = new AuthoritativeProfileManager(originalBinding);
        var session = new SyntheticBrowserSession();
        var sessionFactory = new CapturingSessionFactory(session);
        var registration = new TrackingSessionRegistration();
        var ephemeralFactory = new CapturingEphemeralProfileFactory();
        await using var orchestrator = CreateOrchestrator(
            session,
            factory: sessionFactory,
            profileManager: profileManager,
            ephemeralProfileFactory: ephemeralFactory,
            sessionRegistration: registration);
        var relogin = new CleanBrowserReloginService(
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            orchestrator);

        var result = await relogin.ExecuteAsync(accountId);

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.False(result.IsSuccess);
        Assert.Equal("IDENTITY_UNCONFIRMED", result.Status);
        Assert.NotNull(result.CandidateProfile);
        Assert.Equal(accountId, result.CandidateProfile!.AccountId);
        Assert.Equal(accountId, result.CandidateProfile.Binding.AccountId);
        Assert.NotEqual(originalBinding.BindingId, result.CandidateProfile.BindingId);
        Assert.Equal(result.CandidateProfile, sessionFactory.LastProfile);
        Assert.Equal(accountId, registration.LastRegisteredAccountId);
        Assert.True(registration.RegisteredAttemptIds.Contains(result.AttemptId!.Value));
        Assert.False(registration.UnregisteredAttemptIds.Contains(result.AttemptId.Value));
        Assert.Equal(originalBinding.BindingId, (await profileManager.GetBindingAsync(accountId))!.BindingId);
        Assert.NotEqual(originalBinding.OpaqueProfileIdentifier, ephemeralFactory.LastProfile!.OpaqueProfileIdentifier);
        Assert.False(ephemeralFactory.CleanupCalled, "An unverified successful candidate remains available for explicit promotion.");
    }

    [Fact]
    public async Task RefreshPromotesRetainedCandidateAfterUserCompletesProviderChallenge()
    {
        const string email = "synthetic-pending-candidate@example.test";
        var accountId = await CreateAccountAsync(email);
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var saved = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(email, "SyntheticPendingCandidatePassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(saved.IsSuccess, saved.Message);

        var originalBinding = new BrowserProfileBindingVm(
            Guid.NewGuid(), accountId, "openai-chatgpt", "Chrome", "existing-profile", "Ready",
            DateTimeOffset.UtcNow.AddMinutes(-5), null);
        var profileManager = new AuthoritativeProfileManager(originalBinding);
        var session = new SyntheticBrowserSession();
        var ephemeralFactory = new CapturingEphemeralProfileFactory();
        var identity = new TwoPhaseIdentityVerificationService();
        await using var orchestrator = CreateOrchestrator(
            session,
            profileManager: profileManager,
            ephemeralProfileFactory: ephemeralFactory,
            identityVerificationService: identity);
        var relogin = new CleanBrowserReloginService(
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            orchestrator);

        var initial = await relogin.ExecuteAsync(accountId);
        Assert.Equal(BrowserAuthState.BrowserSessionObserved, initial.State);
        Assert.False(initial.IsSuccess);
        Assert.Equal(IdentityVerificationStatus.Unverified, initial.ObservationResult?.IdentityStatus);
        Assert.Null(profileManager.PromotedCandidate);
        Assert.False(ephemeralFactory.CleanupCalled);

        var resumed = await orchestrator.RefreshAsync(initial.AttemptId!.Value);

        Assert.True(resumed.IsSuccess, resumed.Message);
        Assert.Equal(IdentityVerificationStatus.IdentityVerified, resumed.ObservationResult?.IdentityStatus);
        Assert.Equal(initial.CandidateProfile?.BindingId, profileManager.PromotedCandidate?.BindingId);
        Assert.False(ephemeralFactory.CleanupCalled);

        await orchestrator.DisposeAsync();
        Assert.False(ephemeralFactory.CleanupCalled, "A promoted profile directory now belongs to the durable account binding.");
    }

    [Fact]
    public async Task CleanReloginHumanChallengeRetainsCandidateForManualContinuation()
    {
        var accountId = await CreateAccountAsync("synthetic-clean-relogin-failure@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(
                "synthetic-clean-relogin-failure@example.test",
                "SyntheticCleanReloginFailurePassword-123",
                "JBSWY3DPEHPK3PXP",
                false));
        Assert.True(save.IsSuccess, save.Message);

        var session = new SyntheticBrowserSession { State = BrowserPageState.Captcha };
        var registration = new TrackingSessionRegistration();
        var ephemeralFactory = new CapturingEphemeralProfileFactory();
        await using var orchestrator = CreateOrchestrator(
            session,
            ephemeralProfileFactory: ephemeralFactory,
            sessionRegistration: registration);
        var relogin = new CleanBrowserReloginService(
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            orchestrator);

        var result = await relogin.ExecuteAsync(accountId);

        Assert.Equal(BrowserAuthState.HumanActionRequired, result.State);
        Assert.False(session.IsDisposed, "A manual challenge must keep the exact managed browser context alive.");
        Assert.Contains(result.AttemptId!.Value, registration.RegisteredAttemptIds);
        Assert.DoesNotContain(result.AttemptId.Value, registration.UnregisteredAttemptIds);
        Assert.False(ephemeralFactory.CleanupCalled, "The candidate profile must remain available until the bounded continuation lease expires.");
        Assert.True(await AccountExistsAsync(accountId), "Candidate cleanup must not delete or recreate the selected account.");

        // Simulate the user completing the provider challenge in the same
        // visible Chrome window. Refresh must observe the page without sending
        // the saved password or TOTP a second time.
        session.State = BrowserPageState.Authenticated;
        var resumed = await orchestrator.RefreshAsync(result.AttemptId.Value);

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, resumed.State);
        Assert.Empty(session.FilledValues);
        Assert.False(session.IsDisposed);
        Assert.False(ephemeralFactory.CleanupCalled);
    }

    [Fact]
    public async Task AutoLoginRequiresCredentialRotationAttestationBeforeOpeningBrowser()
    {
        var accountId = await CreateAccountAsync("synthetic-attestation@example.test");
        var session = new SyntheticBrowserSession();
        await using var orchestrator = CreateOrchestrator(session);

        var result = await orchestrator.AutoLoginAsync(accountId);

        Assert.Equal(BrowserAuthState.Failed, result.State);
        Assert.Equal("CREDENTIAL_ROTATION_ATTESTATION_REQUIRED", result.Status);
        Assert.Empty(session.FilledValues);
        Assert.False(session.IsDisposed);
    }

    [Fact]
    public async Task ConcurrentAutoLoginForOneAccountCreatesOnlyOneAttempt()
    {
        var accountId = await CreateAccountAsync("synthetic-duplicate-attempt@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(
                "synthetic-duplicate-attempt@example.test",
                "SyntheticDuplicatePassword-123",
                "JBSWY3DPEHPK3PXP",
                false));
        Assert.True(save.IsSuccess, save.Message);

        var session = new SyntheticBrowserSession();
        var factory = new BlockingSessionFactory(session);
        await using var orchestrator = CreateOrchestrator(session, factory);

        var first = orchestrator.AutoLoginAsync(accountId, credentialRotationAttested: true);
        await factory.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var duplicate = await orchestrator.AutoLoginAsync(accountId, credentialRotationAttested: true);

        Assert.Equal(BrowserAuthState.Failed, duplicate.State);
        Assert.Equal("AUTO_LOGIN_IN_PROGRESS", duplicate.Status);
        Assert.Null(duplicate.AttemptId);

        factory.Release.TrySetResult();
        var completed = await first;
        Assert.Equal(BrowserAuthState.BrowserSessionObserved, completed.State);
        Assert.NotNull(completed.AttemptId);
    }

    [Fact]
    public async Task AutoLoginExposesBoundedRedactedStageTimingAndLeavesUnexecutedStagesUnset()
    {
        var accountId = await CreateAccountAsync("synthetic-timing@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-timing@example.test", "SyntheticTimingPassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);
        var session = new SyntheticBrowserSession();
        await using var orchestrator = CreateOrchestrator(session);

        var preflight = await orchestrator.GetAutoLoginPreflightAsync(accountId);
        Assert.NotNull(preflight.Timing);
        Assert.InRange(preflight.Timing!.PreflightDurationMs!.Value, 0, 10_000);

        var result = await orchestrator.AutoLoginAsync(accountId, credentialRotationAttested: true);

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.NotNull(result.Timing);
        var timing = result.Timing!;
        Assert.NotNull(timing.ProfileLeaseDurationMs);
        Assert.NotNull(timing.ExistingSessionCheckDurationMs);
        Assert.NotNull(timing.EmailStepDurationMs);
        Assert.NotNull(timing.PasswordStepDurationMs);
        Assert.NotNull(timing.MfaStepDurationMs);
        Assert.NotNull(timing.AuthenticatedObservationDurationMs);
        Assert.Null(timing.PreflightDurationMs);
        Assert.Null(timing.ChromeLaunchDurationMs);
        Assert.Null(timing.OfficialNavigationDurationMs);
        Assert.Null(timing.LoginCtaDurationMs);
        Assert.InRange(timing.TotalAttemptDurationMs!.Value, 0, 30_000);

        var executedStageDurations = new[]
        {
            timing.ProfileLeaseDurationMs,
            timing.ExistingSessionCheckDurationMs,
            timing.EmailStepDurationMs,
            timing.PasswordStepDurationMs,
            timing.MfaStepDurationMs,
            timing.AuthenticatedObservationDurationMs
        };
        Assert.All(executedStageDurations, duration =>
            Assert.InRange(duration!.Value, 0, timing.TotalAttemptDurationMs.Value + 1_000));

        var safeDiagnosticText = $"{result.Timing} {result.Message} {result.Status}";
        Assert.DoesNotContain("SyntheticTimingPassword-123", safeDiagnosticText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoLoginAuthenticatedFastPathDoesNotReadVault()
    {
        var accountId = await CreateAccountAsync("synthetic-session@example.test");
        var session = new SyntheticBrowserSession { State = BrowserPageState.Authenticated };
        await using var orchestrator = CreateOrchestrator(session);

        var result = await orchestrator.AutoLoginAsync(accountId, credentialRotationAttested: true);

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.Empty(session.FilledValues);
    }

    [Fact]
    public async Task AutoLoginPreflightRequiresCredentialReferenceAndAutomaticAccess()
    {
        var accountId = await CreateAccountAsync("synthetic-preflight@example.test");
        await using var orchestrator = CreateOrchestrator(new SyntheticBrowserSession());

        var missing = await orchestrator.GetAutoLoginPreflightAsync(accountId);
        Assert.False(missing.IsReady);
        Assert.Equal("CREDENTIAL_NOT_FOUND", missing.Status);

        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-preflight@example.test", "SyntheticPreflightPassword-123", null, false));
        Assert.True(save.IsSuccess, save.Message);
        var ready = await orchestrator.GetAutoLoginPreflightAsync(accountId);
        Assert.True(ready.IsReady, ready.Message);
        Assert.True(ready.CredentialReferenceReady);
        Assert.Equal("https://chatgpt.com/", ready.OfficialDestination);
    }

    [Fact]
    public async Task AutoLoginReadsCompatibleCredentialWithoutInteractiveUnlock()
    {
        var accountId = await CreateAccountAsync("synthetic-vault-gate@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-vault-gate@example.test", "SyntheticVaultGatePassword-123", null, false));
        Assert.True(save.IsSuccess, save.Message);

        var session = new SyntheticBrowserSession();
        await using var orchestrator = CreateOrchestrator(session);

        var result = await orchestrator.AutoLoginAsync(accountId, credentialRotationAttested: true);

        Assert.Equal(BrowserAuthState.MfaRequired, result.State);
        Assert.Equal("MFA_SEED_MISSING", result.Status);
        Assert.Equal("synthetic-vault-gate@example.test", session.FilledValues[BrowserAuthField.Email]);
        Assert.Equal("SyntheticVaultGatePassword-123", session.FilledValues[BrowserAuthField.Password]);
    }

    [Fact]
    public async Task AutoLoginPreservesHumanTakeoverBoundary()
    {
        var accountId = await CreateAccountAsync("synthetic-takeover@example.test");
        var session = new SyntheticBrowserSession { State = BrowserPageState.Captcha };
        await using var orchestrator = CreateOrchestrator(session);

        var result = await orchestrator.AutoLoginAsync(accountId, credentialRotationAttested: true);

        Assert.Equal(BrowserAuthState.HumanActionRequired, result.State);
        Assert.Equal("CAPTCHA_REQUIRED", result.Status);
        Assert.Empty(session.FilledValues);
        Assert.True(await AccountExistsAsync(accountId), "Human takeover must not delete or recreate the selected account.");
    }

    [Fact]
    public void AutoLoginConsentIsBoundAndSingleUse()
    {
        var attemptId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var consent = new AutoLoginAttemptConsent(
            attemptId,
            accountId,
            "openai-chatgpt",
            bindingId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.True(consent.IsActive(DateTimeOffset.UtcNow));
        Assert.True(consent.Allows(BrowserAuthAction.FillEmail));
        Assert.True(consent.Allows(BrowserAuthAction.FillPassword));
        Assert.True(consent.Allows(BrowserAuthAction.FillTotp));

        consent.Consume();
        Assert.False(consent.IsActive(DateTimeOffset.UtcNow));
        consent.Revoke();
        Assert.True(consent.IsRevoked);
    }

    [Fact]
    public async Task UnknownOriginIsBlockedBeforeSensitiveFill()
    {
        var accountId = await CreateAccountAsync("origin-auth@example.test");
        var session = new SyntheticBrowserSession
        {
            CurrentUriOverride = new Uri("https://evil.example.test/login")
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);

        Assert.Equal(BrowserAuthState.Failed, started.State);
        Assert.Equal("BROWSER_ORIGIN_UNTRUSTED", started.Status);
        Assert.Empty(session.FilledValues);
        Assert.True(await AccountExistsAsync(accountId), "Origin rejection must not delete or recreate the selected account.");
    }

    [Fact]
    public async Task CaptchaStopsForHumanTakeover()
    {
        var accountId = await CreateAccountAsync("challenge-auth@example.test");
        var session = new SyntheticBrowserSession { State = BrowserPageState.Captcha };
        await using var orchestrator = CreateOrchestrator(session);

        var result = await orchestrator.StartAsync(accountId);

        Assert.Equal(BrowserAuthState.HumanActionRequired, result.State);
        Assert.Equal("CAPTCHA_REQUIRED", result.Status);
        Assert.Null(result.NextAction);
        Assert.True(await AccountExistsAsync(accountId), "Authentication failure must not delete or recreate the selected account.");
    }

    [Fact]
    public async Task ProfileLockAndCancellationRemainExplicit()
    {
        var accountId = await CreateAccountAsync("locked-auth@example.test");
        var profile = new SyntheticProfileManager();
        await using var lockedOrchestrator = CreateOrchestrator(new SyntheticBrowserSession(), new ThrowingSessionFactory("PROFILE_IN_USE"), profile);

        var locked = await lockedOrchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.ProfileInUse, locked.State);
        Assert.Equal("PROFILE_IN_USE", locked.Status);

        var session = new SyntheticBrowserSession();
        await using var orchestrator = CreateOrchestrator(session);
        var started = await orchestrator.StartAsync(accountId);
        var cancelled = await orchestrator.CancelAsync(started.AttemptId!.Value);

        Assert.Equal(BrowserAuthState.Cancelled, cancelled.State);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public async Task CredentialOwnershipIsEnforcedBySelectedAccount()
    {
        var owner = await CreateAccountAsync("owner-auth@example.test");
        var other = await CreateAccountAsync("other-auth@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            owner,
            new ChatGptCredentialInput("owner-auth@example.test", "OwnerPassword-123", null, false));
        Assert.True(save.IsSuccess, save.Message);
        var session = new SyntheticBrowserSession { State = BrowserPageState.PasswordRequired };
        await using var orchestrator = CreateOrchestrator(session);
        var started = await orchestrator.StartAsync(other);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillPassword, confirmed: true);

        Assert.Equal("CREDENTIAL_NOT_FOUND", result.Status);
        Assert.False(session.FilledValues.ContainsKey(BrowserAuthField.Password));
    }

    private BrowserAuthOrchestrator CreateOrchestrator(
        IBrowserAuthenticationSession session,
        IBrowserAuthenticationSessionFactory? factory = null,
        IBrowserProfileManager? profileManager = null,
        TimeSpan? postLoginObservationBudget = null,
        TimeSpan? postLoginOperationBudget = null,
        IEphemeralBrowserProfileFactory? ephemeralProfileFactory = null,
        IAccountBrowserSessionRegistration? sessionRegistration = null,
        TimeSpan? attemptLifetime = null,
        IAuditSink? auditSink = null,
        TimeSpan? credentialTransitionBudget = null,
        IProviderAccountVerificationService? identityVerificationService = null)
    {
        return new BrowserAuthOrchestrator(
            profileManager ?? new SyntheticProfileManager(),
            factory ?? new SyntheticSessionFactory(session),
            new StableBrowserPageStateDetector(),
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            new Rfc6238TotpGenerator(),
            ephemeralProfileFactory: ephemeralProfileFactory,
            sessionRegistration: sessionRegistration,
            auditSink: auditSink,
            postLoginObservationBudget: postLoginObservationBudget,
            postLoginOperationBudget: postLoginOperationBudget,
            attemptLifetime: attemptLifetime,
            credentialTransitionBudget: credentialTransitionBudget,
            identityVerificationService: identityVerificationService);
    }

    private async Task<Guid> CreateAccountAsync(string email)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = email.Split('@')[0],
            Provider = "openai-chatgpt",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(result.IsSuccess, result.Message);

        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email)).Single().AccountId;
    }

    private async Task<bool> AccountExistsAsync(Guid accountId)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return await reader.GetAccountDetailAsync(accountId) is not null;
    }

    private sealed class SyntheticProfileManager : IBrowserProfileManager
    {
        public Task<BrowserProfileBindingVm?> GetBindingAsync(Guid accountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
            => Task.FromResult<BrowserProfileBindingVm?>(new BrowserProfileBindingVm(
                Guid.NewGuid(), accountId, providerId, "Synthetic", "synthetic-profile", "Ready", DateTimeOffset.UtcNow, null));

        public Task<BrowserProfilePreparationResult> PrepareAsync(Guid accountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
        {
            var binding = new BrowserProfileBindingVm(Guid.NewGuid(), accountId, providerId, "Synthetic", "synthetic-profile", "Ready", DateTimeOffset.UtcNow, null);
            return Task.FromResult(new BrowserProfilePreparationResult(
                true,
                "BrowserProfileReady",
                "Synthetic profile ready.",
                new BrowserProfileSessionContext(
                    binding.BindingId,
                    accountId,
                    providerId,
                    binding.OpaqueProfileIdentifier,
                    Path.Combine(Path.GetTempPath(), "flash-router-synthetic-profile"),
                    "synthetic-chrome",
                    binding)));
        }

        public Task RecordLaunchResultAsync(Guid bindingId, bool succeeded, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SyntheticSessionFactory(IBrowserAuthenticationSession session) : IBrowserAuthenticationSessionFactory
    {
        public Task<IBrowserAuthenticationSession> OpenAsync(BrowserProfileSessionContext profile, Uri officialUri, CancellationToken cancellationToken = default)
            => Task.FromResult(session);
    }

    private sealed class ThrowingSessionFactory(string code) : IBrowserAuthenticationSessionFactory
    {
        public Task<IBrowserAuthenticationSession> OpenAsync(BrowserProfileSessionContext profile, Uri officialUri, CancellationToken cancellationToken = default)
            => Task.FromException<IBrowserAuthenticationSession>(new BrowserAuthSessionException(code, "Synthetic profile lock."));
    }

    private sealed class BlockingSessionFactory(IBrowserAuthenticationSession session) : IBrowserAuthenticationSessionFactory
    {
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IBrowserAuthenticationSession> OpenAsync(
            BrowserProfileSessionContext profile,
            Uri officialUri,
            CancellationToken cancellationToken = default)
        {
            Opened.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return session;
        }
    }

    private sealed class InlineProgress(List<BrowserAuthProgress> items) : IProgress<BrowserAuthProgress>
    {
        public void Report(BrowserAuthProgress value) => items.Add(value);
    }

    private sealed class AuthoritativeProfileManager(BrowserProfileBindingVm originalBinding)
        : IBrowserProfileManager, IBrowserProfilePromotionService
    {
        public BrowserProfileSessionContext? PromotedCandidate { get; private set; }

        public Task<BrowserProfileBindingVm?> GetBindingAsync(
            Guid accountId,
            string providerId = "openai-chatgpt",
            CancellationToken cancellationToken = default)
            => Task.FromResult<BrowserProfileBindingVm?>(originalBinding);

        public Task<BrowserProfilePreparationResult> PrepareAsync(
            Guid accountId,
            string providerId = "openai-chatgpt",
            CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserProfilePreparationResult(
                true,
                "BrowserProfileReady",
                "Synthetic authoritative profile ready.",
                new BrowserProfileSessionContext(
                    originalBinding.BindingId,
                    accountId,
                    providerId,
                    originalBinding.OpaqueProfileIdentifier,
                    Path.Combine(Path.GetTempPath(), "flash-router-original-profile"),
                    "synthetic-chrome",
                    originalBinding)));

        public Task RecordLaunchResultAsync(
            Guid bindingId,
            bool succeeded,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<BrowserProfilePromotionResult> PromoteCandidateAsync(
            Guid accountId,
            BrowserProfileSessionContext candidate,
            IdentityVerificationStatus identityStatus,
            CancellationToken cancellationToken = default)
        {
            if (accountId != candidate.AccountId || identityStatus != IdentityVerificationStatus.IdentityVerified)
            {
                return Task.FromResult(BrowserProfilePromotionResult.Failure(
                    "PROFILE_PROMOTION_IDENTITY_NOT_VERIFIED",
                    "Synthetic promotion requires the verified selected account."));
            }

            PromotedCandidate = candidate;
            return Task.FromResult(new BrowserProfilePromotionResult(
                true,
                "PROFILE_PROMOTION_COMMITTED",
                "Synthetic account profile promoted.",
                candidate.Binding));
        }
    }

    private sealed class TwoPhaseIdentityVerificationService : IProviderAccountVerificationService
    {
        private int _calls;

        public Task<ProviderIdentityVerificationResult> VerifyAsync(
            ProviderIdentityVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            var status = Interlocked.Increment(ref _calls) == 1
                ? IdentityVerificationStatus.Unverified
                : IdentityVerificationStatus.IdentityVerified;
            return Task.FromResult(new ProviderIdentityVerificationResult(
                request.AccountId,
                request.AttemptId,
                request.BrowserProfileBindingId,
                request.ProviderId,
                status,
                DateTimeOffset.UtcNow,
                "Synthetic visible account evidence",
                status == IdentityVerificationStatus.IdentityVerified ? request.IntendedAccountEmail : null,
                status == IdentityVerificationStatus.IdentityVerified ? null : "IDENTITY_EVIDENCE_INSUFFICIENT"));
        }
    }

    private sealed class CapturingEphemeralProfileFactory : IEphemeralBrowserProfileFactory
    {
        public BrowserProfileSessionContext? LastProfile { get; private set; }
        public bool CleanupCalled { get; private set; }

        public Task<EphemeralBrowserProfilePreparationResult> PrepareAsync(
            BrowserSessionMode sessionMode,
            CancellationToken cancellationToken = default)
        {
            var bindingId = Guid.NewGuid();
            var profileId = "candidate-" + Guid.NewGuid().ToString("N");
            LastProfile = new BrowserProfileSessionContext(
                bindingId,
                Guid.Empty,
                "openai-chatgpt",
                profileId,
                Path.Combine(Path.GetTempPath(), "flash-router-candidate-" + Guid.NewGuid().ToString("N")),
                "synthetic-chrome",
                new BrowserProfileBindingVm(
                    bindingId,
                    Guid.Empty,
                    "openai-chatgpt",
                    "Chrome",
                    profileId,
                    "Available",
                    DateTimeOffset.UtcNow,
                    null));
            return Task.FromResult(new EphemeralBrowserProfilePreparationResult(
                true,
                "EphemeralBrowserProfileReady",
                "Synthetic clean profile ready.",
                LastProfile,
                () =>
                {
                    CleanupCalled = true;
                    return ValueTask.CompletedTask;
                }));
        }
    }

    private sealed class CapturingSessionFactory(IBrowserAuthenticationSession session) : IBrowserAuthenticationSessionFactory
    {
        public BrowserProfileSessionContext? LastProfile { get; private set; }

        public Task<IBrowserAuthenticationSession> OpenAsync(
            BrowserProfileSessionContext profile,
            Uri officialUri,
            CancellationToken cancellationToken = default)
        {
            LastProfile = profile;
            return Task.FromResult(session);
        }
    }

    private sealed class TrackingSessionRegistration : IAccountBrowserSessionRegistration
    {
        public Guid LastRegisteredAccountId { get; private set; }
        public List<Guid> RegisteredAttemptIds { get; } = [];
        public List<Guid> UnregisteredAttemptIds { get; } = [];

        public Task RegisterManagedSessionAsync(
            Guid accountId,
            Guid attemptId,
            BrowserProfileSessionContext profile,
            IBrowserAuthenticationSession session,
            CancellationToken cancellationToken = default)
        {
            LastRegisteredAccountId = accountId;
            RegisteredAttemptIds.Add(attemptId);
            return Task.CompletedTask;
        }

        public Task UnregisterManagedSessionAsync(
            Guid accountId,
            Guid attemptId,
            CancellationToken cancellationToken = default)
        {
            UnregisteredAttemptIds.Add(attemptId);
            return Task.CompletedTask;
        }
    }

    private sealed class InvalidCredentialSessionFactory : IBrowserAuthenticationSessionFactory
    {
        public List<InvalidCredentialSession> Sessions { get; } = [];

        public Task<IBrowserAuthenticationSession> OpenAsync(
            BrowserProfileSessionContext profile,
            Uri officialUri,
            CancellationToken cancellationToken = default)
        {
            var session = new InvalidCredentialSession();
            Sessions.Add(session);
            return Task.FromResult<IBrowserAuthenticationSession>(session);
        }
    }

    private sealed class InvalidCredentialSession : IBrowserAuthenticationSession
    {
        private BrowserPageState _state = BrowserPageState.EmailRequired;

        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public bool IsDisposed { get; private set; }

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.PasswordRequired;
            return Task.CompletedTask;
        }

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.InvalidCredentials;
            return Task.CompletedTask;
        }

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserPageSnapshot(
                CurrentUri,
                _state,
                HasEmailField: _state == BrowserPageState.EmailRequired,
                HasPasswordField: _state == BrowserPageState.PasswordRequired));

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DelayedCredentialTransitionSession(int readsBeforeTransition) : IBrowserAuthenticationSession
    {
        private BrowserPageState _state = BrowserPageState.EmailRequired;
        private int _readsBeforeTransition = readsBeforeTransition;
        private bool _passwordSubmitted;

        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public int PasswordSubmissionCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.PasswordRequired;
            return Task.CompletedTask;
        }

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        {
            _passwordSubmitted = true;
            PasswordSubmissionCount++;
            return Task.CompletedTask;
        }

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
        {
            if (_passwordSubmitted && _readsBeforeTransition <= 0)
            {
                _state = BrowserPageState.Authenticated;
            }
            else if (_passwordSubmitted && _readsBeforeTransition != int.MaxValue)
            {
                _readsBeforeTransition--;
            }

            return Task.FromResult(new BrowserPageSnapshot(
                CurrentUri,
                _state,
                HasEmailField: _state == BrowserPageState.EmailRequired,
                HasPasswordField: _state == BrowserPageState.PasswordRequired));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StalledPostLoginSession : IBrowserAuthenticationSession
    {
        private BrowserPageState _state = BrowserPageState.EmailRequired;

        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public bool IsDisposed { get; private set; }
        public int TotpSubmissionCount { get; private set; }
        public bool ThrowOnNextRead { get; set; }
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _releaseToAuthenticated;

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.PasswordRequired;
            return Task.CompletedTask;
        }

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.MfaRequired;
            return Task.CompletedTask;
        }

        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default)
        {
            TotpSubmissionCount++;
            _state = BrowserPageState.Unknown;
            return Task.CompletedTask;
        }

        public void ReleaseToAuthenticated()
        {
            _releaseToAuthenticated = true;
            _state = BrowserPageState.Authenticated;
        }

        public async Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnNextRead)
            {
                ThrowOnNextRead = false;
                throw new BrowserAuthSessionException("BROWSER_CLOSED", "Synthetic browser context closed.");
            }

            if (_state == BrowserPageState.Unknown && !_releaseToAuthenticated)
            {
                ReadEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new BrowserPageSnapshot(CurrentUri, _state);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PostTotpMfaSession(MfaObservationKind observation) : IBrowserAuthenticationSession
    {
        private BrowserPageState _state = BrowserPageState.EmailRequired;

        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public bool IsDisposed { get; private set; }
        public int TotpSubmissionCount { get; private set; }

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default)
        {
            if (field == BrowserAuthField.Totp)
            {
                TotpSubmissionCount++;
            }

            return Task.CompletedTask;
        }

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.PasswordRequired;
            return Task.CompletedTask;
        }

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.MfaRequired;
            return Task.CompletedTask;
        }

        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.MfaRequired;
            return Task.CompletedTask;
        }

        public void ResolveToAuthenticated() => _state = BrowserPageState.Authenticated;

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserPageSnapshot(
                CurrentUri,
                _state,
                HasEmailField: _state == BrowserPageState.EmailRequired,
                HasPasswordField: _state == BrowserPageState.PasswordRequired,
                HasTotpField: _state == BrowserPageState.MfaRequired,
                MfaObservation: _state == BrowserPageState.MfaRequired ? observation : MfaObservationKind.AuthenticatedShellObserved));

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingAuditSink : IAuditSink
    {
        public List<AuditEvent> Events { get; } = [];

        public ValueTask WriteAsync(
            Guid? accountId,
            string eventType,
            string safeSummary,
            string severity = "Info",
            string? safeDetails = null,
            string? correlationId = null,
            Guid? attemptId = null,
            string? providerId = null,
            string? source = null,
            string? outcome = null,
            string? errorCode = null,
            long? durationMs = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new AuditEvent(eventType, severity, errorCode));
            return ValueTask.CompletedTask;
        }

        public sealed record AuditEvent(string EventType, string Severity, string? ErrorCode);
    }

    private sealed class TransientPostLoginObservationSession : IBrowserAuthenticationSession
    {
        private readonly string _failureCode;
        private int _remainingFailures;
        private BrowserPageState _state = BrowserPageState.EmailRequired;

        public TransientPostLoginObservationSession(string failureCode, int failureCount)
        {
            _failureCode = failureCode;
            _remainingFailures = failureCount;
        }

        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public Dictionary<BrowserAuthField, int> FillCounts { get; } = [];
        public int TransientObservationCount { get; private set; }

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default)
        {
            FillCounts[field] = FillCounts.TryGetValue(field, out var count) ? count + 1 : 1;
            return Task.CompletedTask;
        }

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.PasswordRequired;
            return Task.CompletedTask;
        }

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.MfaRequired;
            return Task.CompletedTask;
        }

        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default)
        {
            _state = BrowserPageState.Authenticated;
            return Task.CompletedTask;
        }

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
        {
            if (_state == BrowserPageState.Authenticated && _remainingFailures > 0)
            {
                _remainingFailures--;
                TransientObservationCount++;
                return Task.FromResult(new BrowserPageSnapshot(
                    CurrentUri,
                    BrowserPageState.Unknown,
                    IsPageReady: false,
                    ObservationFailureCode: _failureCode,
                    ObservationFailureMessage: "Synthetic transient observation failure."));
            }

            return Task.FromResult(new BrowserPageSnapshot(
                CurrentUri,
                _state,
                HasEmailField: _state == BrowserPageState.EmailRequired,
                HasPasswordField: _state == BrowserPageState.PasswordRequired,
                HasTotpField: _state == BrowserPageState.MfaRequired));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SyntheticBrowserSession : IBrowserAuthenticationSession
    {
        public BrowserPageState State { get; set; } = BrowserPageState.EmailRequired;
        public Uri? CurrentUriOverride { get; set; }
        public Dictionary<BrowserAuthField, string> FilledValues { get; } = [];
        public bool IsDisposed { get; private set; }
        public Uri CurrentUri => CurrentUriOverride ?? BrowserOriginPolicy.ChatGptUri;

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            CurrentUriOverride = uri;
            return Task.CompletedTask;
        }

        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default)
        {
            State = BrowserPageState.EmailRequired;
            return Task.CompletedTask;
        }

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserPageSnapshot(
                CurrentUri,
                State,
                State == BrowserPageState.EmailRequired,
                State == BrowserPageState.PasswordRequired,
                State == BrowserPageState.MfaRequired));

        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default)
        {
            if (!BrowserOriginPolicy.IsAllowed(CurrentUri))
            {
                throw new BrowserAuthSessionException("BROWSER_ORIGIN_UNTRUSTED", "Synthetic origin blocked.");
            }

            FilledValues[field] = value;
            return Task.CompletedTask;
        }

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        {
            State = BrowserPageState.PasswordRequired;
            return Task.CompletedTask;
        }

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        {
            State = BrowserPageState.MfaRequired;
            return Task.CompletedTask;
        }

        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default)
        {
            State = BrowserPageState.Authenticated;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
