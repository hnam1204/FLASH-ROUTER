using System.Collections.Concurrent;
using System.Security.Cryptography;
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
        Assert.Equal("BrowserSessionObserved", mfa.Observation is null ? null : "BrowserSessionObserved");
        Assert.Equal(6, session.FilledValues[BrowserAuthField.Totp].Length);
        Assert.Equal("BROWSER_SESSION_OBSERVED", mfa.Status);
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
    public async Task StalledPostLoginObservationReturnsBoundedTimeoutWithTerminalProgress()
    {
        var accountId = await CreateAccountAsync("synthetic-stalled@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-stalled@example.test", "SyntheticStalledPassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);

        var progress = new List<BrowserAuthProgress>();
        await using var orchestrator = CreateOrchestrator(
            new StalledPostLoginSession(),
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
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.Contains(progress, item => item.State == BrowserAuthState.ObservingPostLogin);
        Assert.Contains(progress, item => item.State == BrowserAuthState.TimedOut && item.IsTerminal);
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
    }

    [Fact]
    public async Task CleanReloginFailureReleasesCandidateSessionRegistration()
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
        await using var orchestrator = CreateOrchestrator(
            session,
            ephemeralProfileFactory: new CapturingEphemeralProfileFactory(),
            sessionRegistration: registration);
        var relogin = new CleanBrowserReloginService(
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            orchestrator);

        var result = await relogin.ExecuteAsync(accountId);

        Assert.Equal(BrowserAuthState.HumanActionRequired, result.State);
        Assert.True(session.IsDisposed);
        Assert.Contains(result.AttemptId!.Value, registration.RegisteredAttemptIds);
        Assert.Contains(result.AttemptId.Value, registration.UnregisteredAttemptIds);
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
        IAccountBrowserSessionRegistration? sessionRegistration = null)
    {
        return new BrowserAuthOrchestrator(
            profileManager ?? new SyntheticProfileManager(),
            factory ?? new SyntheticSessionFactory(session),
            new StableBrowserPageStateDetector(),
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            new Rfc6238TotpGenerator(),
            ephemeralProfileFactory: ephemeralProfileFactory,
            sessionRegistration: sessionRegistration,
            postLoginObservationBudget: postLoginObservationBudget,
            postLoginOperationBudget: postLoginOperationBudget);
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

    private sealed class AuthoritativeProfileManager(BrowserProfileBindingVm originalBinding) : IBrowserProfileManager
    {
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
    }

    private sealed class CapturingEphemeralProfileFactory : IEphemeralBrowserProfileFactory
    {
        public BrowserProfileSessionContext? LastProfile { get; private set; }

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
                LastProfile));
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

    private sealed class StalledPostLoginSession : IBrowserAuthenticationSession
    {
        private BrowserPageState _state = BrowserPageState.EmailRequired;

        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public bool IsDisposed { get; private set; }

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
            _state = BrowserPageState.Unknown;
            return Task.CompletedTask;
        }

        public async Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
        {
            if (_state == BrowserPageState.Unknown)
            {
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
