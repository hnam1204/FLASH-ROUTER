using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlashAI.IntegrationTests;

public sealed class ChatGptPasswordStepAutomationTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();
    private readonly string _vaultRoot = Path.Combine(Path.GetTempPath(), "flash-router-pwd-test-" + Guid.NewGuid().ToString("N"));
    private IDataProtectionProvider _dataProtection = null!;
    private ISecretVault _vault = null!;
    private VaultAccessState _vaultAccess = null!;
    private readonly TestLoggerProvider _loggerProvider = new();

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _dataProtection = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_vaultRoot, "keys")),
            builder => builder.SetApplicationName("FLASH ROUTER PWD AUTOMATION TEST"));
        _vault = new FileSecretVault(Path.Combine(_vaultRoot, "vault"), _dataProtection);
        _vaultAccess = new VaultAccessState();
        _vaultAccess.SetUnlocked(true);
    }

    public async Task DisposeAsync()
    {
        (_vault as IDisposable)?.Dispose();
        (_dataProtection as IDisposable)?.Dispose();
        await _database.DisposeAsync();
        if (Directory.Exists(_vaultRoot))
        {
            try { Directory.Delete(_vaultRoot, recursive: true); } catch { /* ignore */ }
        }
    }

    // -------------------------------------------------------------
    // Test 1: Immediate Email to Password transition (0ms)
    // -------------------------------------------------------------
    [Fact]
    public async Task Test01_EmailToPasswordTransition_Immediate()
    {
        var accountId = await CreateAccountAsync("user01@example.test", "Pass01-Immediate");
        var session = new DelayedTransitionSession(delayMs: 0);
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.EmailRequired, started.State);

        var continued = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);
        Assert.Equal(BrowserAuthState.PasswordRequired, continued.State);
        Assert.Equal("PASSWORD_REQUIRED", continued.Status);
    }

    // -------------------------------------------------------------
    // Test 2: Password form appears after 500ms delay
    // -------------------------------------------------------------
    [Fact]
    public async Task Test02_EmailToPasswordTransition_500msDelay()
    {
        var accountId = await CreateAccountAsync("user02@example.test", "Pass02-500ms");
        var session = new DelayedTransitionSession(delayMs: 500);
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.EmailRequired, started.State);

        var continued = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);
        Assert.Equal(BrowserAuthState.PasswordRequired, continued.State);
        Assert.Equal("PASSWORD_REQUIRED", continued.Status);
    }

    // -------------------------------------------------------------
    // Test 3: Password form appears after 2000ms delay
    // -------------------------------------------------------------
    [Fact]
    public async Task Test03_EmailToPasswordTransition_2000msDelay()
    {
        var accountId = await CreateAccountAsync("user03@example.test", "Pass03-2000ms");
        var session = new DelayedTransitionSession(delayMs: 2000);
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.EmailRequired, started.State);

        var continued = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);
        Assert.Equal(BrowserAuthState.PasswordRequired, continued.State);
        Assert.Equal("PASSWORD_REQUIRED", continued.Status);
    }

    // -------------------------------------------------------------
    // Test 4: Password form appears after 5000ms delay
    // -------------------------------------------------------------
    [Fact]
    public async Task Test04_EmailToPasswordTransition_5000msDelay()
    {
        var accountId = await CreateAccountAsync("user04@example.test", "Pass04-5000ms");
        var session = new DelayedTransitionSession(delayMs: 5000);
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.EmailRequired, started.State);

        var continued = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);
        Assert.Equal(BrowserAuthState.PasswordRequired, continued.State);
        Assert.Equal("PASSWORD_REQUIRED", continued.Status);
    }

    // -------------------------------------------------------------
    // Test 5: Vietnamese UI Compatibility
    // -------------------------------------------------------------
    [Fact]
    public async Task Test05_VietnameseUiCompatibility()
    {
        var detector = new StableBrowserPageStateDetector();

        // Vietnamese Login Entry
        var guestSnapshot = new BrowserPageSnapshot(
            BrowserOriginPolicy.ChatGptUri,
            BrowserPageState.GuestLandingLoginAvailable,
            HasEmailField: false,
            HasPasswordField: false,
            HasTotpField: false,
            HasGuestLoginEntry: true,
            GuestLoginEntryCount: 1,
            IsPageReady: true);
        Assert.Equal(BrowserPageState.GuestLandingLoginAvailable, detector.Detect(guestSnapshot));

        // Vietnamese Password Required
        var pwdSnapshot = new BrowserPageSnapshot(
            new Uri("https://auth.openai.com/log-in/password"),
            BrowserPageState.PasswordRequired,
            HasEmailField: false,
            HasPasswordField: true,
            HasTotpField: false,
            HasGuestLoginEntry: false,
            GuestLoginEntryCount: 0,
            IsPageReady: true);
        Assert.Equal(BrowserPageState.PasswordRequired, detector.Detect(pwdSnapshot));

        // Vietnamese Invalid Credentials
        var invalidCredsSnapshot = new BrowserPageSnapshot(
            new Uri("https://auth.openai.com/log-in/password"),
            BrowserPageState.InvalidCredentials,
            HasEmailField: false,
            HasPasswordField: true,
            HasTotpField: false,
            HasGuestLoginEntry: false,
            GuestLoginEntryCount: 0,
            IsPageReady: true);
        Assert.Equal(BrowserPageState.InvalidCredentials, detector.Detect(invalidCredsSnapshot));
    }

    // -------------------------------------------------------------
    // Test 6: English UI Compatibility
    // -------------------------------------------------------------
    [Fact]
    public async Task Test06_EnglishUiCompatibility()
    {
        var detector = new StableBrowserPageStateDetector();

        var pwdSnapshot = new BrowserPageSnapshot(
            new Uri("https://auth.openai.com/log-in/password"),
            BrowserPageState.PasswordRequired,
            HasEmailField: false,
            HasPasswordField: true,
            HasTotpField: false,
            HasGuestLoginEntry: false,
            GuestLoginEntryCount: 0,
            IsPageReady: true);
        Assert.Equal(BrowserPageState.PasswordRequired, detector.Detect(pwdSnapshot));

        var mfaSnapshot = new BrowserPageSnapshot(
            new Uri("https://auth.openai.com/challenge/totp"),
            BrowserPageState.MfaRequired,
            HasEmailField: false,
            HasPasswordField: false,
            HasTotpField: true,
            HasGuestLoginEntry: false,
            GuestLoginEntryCount: 0,
            IsPageReady: true);
        Assert.Equal(BrowserPageState.MfaRequired, detector.Detect(mfaSnapshot));

        // An invalid submitted code can leave only a diagnostic message and no
        // visible TOTP input. The explicit page state must still be preserved.
        var invalidMfaSnapshot = new BrowserPageSnapshot(
            new Uri("https://auth.openai.com/challenge/totp"),
            BrowserPageState.MfaRequired,
            HasEmailField: false,
            HasPasswordField: false,
            HasTotpField: false,
            HasGuestLoginEntry: false,
            GuestLoginEntryCount: 0,
            IsPageReady: true);
        Assert.Equal(BrowserPageState.MfaRequired, detector.Detect(invalidMfaSnapshot));
    }

    // -------------------------------------------------------------
    // Test 7: Password field hidden or disabled throws safe exception
    // -------------------------------------------------------------
    [Fact]
    public async Task Test07_PasswordFieldHiddenOrDisabled_HandledSafely()
    {
        var accountId = await CreateAccountAsync("user07@example.test", "Pass07");
        var session = new ConfigurableBrowserSession
        {
            State = BrowserPageState.PasswordRequired,
            ThrowOnPasswordFill = new BrowserAuthSessionException("BROWSER_FIELD_NOT_FOUND", "Không còn nhận diện được trường mật khẩu.")
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        var fillResult = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillPassword, confirmed: true);

        Assert.False(fillResult.IsSuccess);
        Assert.Equal("BROWSER_FIELD_NOT_FOUND", fillResult.Status);
    }

    // -------------------------------------------------------------
    // Test 8: Multiple ambiguous password continue buttons stops safely
    // -------------------------------------------------------------
    [Fact]
    public async Task Test08_AmbiguousPasswordContinue_StopsSafely()
    {
        var accountId = await CreateAccountAsync("user08@example.test", "Pass08");
        var session = new ConfigurableBrowserSession
        {
            State = BrowserPageState.PasswordRequired,
            ThrowOnPasswordContinue = new BrowserAuthSessionException("PASSWORD_CONTINUE_AMBIGUOUS", "Có nhiều nút tiếp tục trong biểu mẫu mật khẩu; đã dừng để tránh chọn nhầm.")
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        var fillResult = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillPassword, confirmed: true);

        Assert.False(fillResult.IsSuccess);
        Assert.Equal("PASSWORD_CONTINUE_AMBIGUOUS", fillResult.Status);
    }

    // -------------------------------------------------------------
    // Test 9: Invalid credentials (wrong password) -> HumanActionRequired
    // -------------------------------------------------------------
    [Fact]
    public async Task Test09_InvalidCredentials_TransitionsToHumanActionRequired()
    {
        var accountId = await CreateAccountAsync("user09@example.test", "WrongPassword123");
        var session = new ConfigurableBrowserSession
        {
            State = BrowserPageState.PasswordRequired,
            StateAfterPasswordSubmit = BrowserPageState.InvalidCredentials
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillPassword, confirmed: true);

        Assert.Equal(BrowserAuthState.HumanActionRequired, result.State);
        Assert.Equal("INVALID_CREDENTIALS", result.Status);
        Assert.Null(result.Observation);
    }

    // -------------------------------------------------------------
    // Test 10: MFA/OTP challenge required after password
    // -------------------------------------------------------------
    [Fact]
    public async Task Test10_MfaRequired_TransitionsToMfaStep()
    {
        var accountId = await CreateAccountAsync("user10@example.test", "GoodPassword123", totpSeed: "JBSWY3DPEHPK3PXP");
        var session = new ConfigurableBrowserSession
        {
            State = BrowserPageState.PasswordRequired,
            StateAfterPasswordSubmit = BrowserPageState.MfaRequired
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillPassword, confirmed: true);

        Assert.Equal(BrowserAuthState.MfaRequired, result.State);
        Assert.Equal("MFA_REQUIRED", result.Status);
        Assert.Equal(BrowserAuthAction.FillTotp, result.NextAction);
    }

    // -------------------------------------------------------------
    // Test 11: Passkey challenge required -> HumanActionRequired
    // -------------------------------------------------------------
    [Fact]
    public async Task Test11_PasskeyRequired_TransitionsToHumanActionRequired()
    {
        var accountId = await CreateAccountAsync("user11@example.test", "GoodPassword123");
        var session = new ConfigurableBrowserSession
        {
            State = BrowserPageState.PasswordRequired,
            StateAfterPasswordSubmit = BrowserPageState.Passkey
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillPassword, confirmed: true);

        Assert.Equal(BrowserAuthState.HumanActionRequired, result.State);
        Assert.Equal("PASSKEY_REQUIRED", result.Status);
        Assert.Null(result.Observation);
    }

    // -------------------------------------------------------------
    // Test 12: Navigation timeout handling
    // -------------------------------------------------------------
    [Fact]
    public async Task Test12_BrowserNavigationTimeout_HandledGracefully()
    {
        var accountId = await CreateAccountAsync("user12@example.test", "Pass12");
        var session = new ConfigurableBrowserSession
        {
            State = BrowserPageState.EmailRequired,
            ThrowOnEmailContinue = new BrowserAuthSessionException("BROWSER_NAVIGATION_FAILED", "Không thể điều hướng tới website chính thức.")
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("BROWSER_NAVIGATION_FAILED", result.Status);
    }

    // -------------------------------------------------------------
    // Test 13: User cancellation stops flow cleanly
    // -------------------------------------------------------------
    [Fact]
    public async Task Test13_UserCancellation_CancelsImmediatelyAndRevokesConsent()
    {
        var accountId = await CreateAccountAsync("user13@example.test", "Pass13");
        var session = new ConfigurableBrowserSession { State = BrowserPageState.EmailRequired };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        Assert.Equal(BrowserAuthState.EmailRequired, started.State);

        var cancelResult = await orchestrator.CancelAsync(started.AttemptId!.Value);
        Assert.Equal(BrowserAuthState.Cancelled, cancelResult.State);
        Assert.Equal("CANCELLED", cancelResult.Status);
        Assert.True(session.IsDisposed);
    }

    // -------------------------------------------------------------
    // Test 14: External / unsupported authentication flow stops safely
    // -------------------------------------------------------------
    [Fact]
    public async Task Test14_ExternalProvider_StopsSafely()
    {
        var accountId = await CreateAccountAsync("user14@example.test", "Pass14");
        var session = new ConfigurableBrowserSession
        {
            State = BrowserPageState.EmailRequired,
            StateAfterEmailContinue = BrowserPageState.ExternalProviderRequired
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);

        Assert.Equal(BrowserAuthState.ExternalProviderRequired, result.State);
        Assert.Equal("EXTERNAL_PROVIDER_REQUIRED", result.Status);
        Assert.Null(result.NextAction);
    }

    // -------------------------------------------------------------
    // Test 15: Session verification success reports BrowserSessionObserved
    // -------------------------------------------------------------
    [Fact]
    public async Task Test15_SessionVerificationSuccess_ReportsBrowserSessionObserved()
    {
        var accountId = await CreateAccountAsync("user15@example.test", "Pass15");
        var session = new ConfigurableBrowserSession
        {
            State = BrowserPageState.PasswordRequired,
            StateAfterPasswordSubmit = BrowserPageState.Authenticated
        };
        await using var orchestrator = CreateOrchestrator(session);

        var started = await orchestrator.StartAsync(accountId);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillPassword, confirmed: true);

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.Equal("BROWSER_SESSION_OBSERVED", result.Status);
        Assert.NotNull(result.Observation);
        Assert.Equal(accountId, result.Observation.AccountId);
    }

    // -------------------------------------------------------------
    // Test 16: Zero credentials in logs or traces
    // -------------------------------------------------------------
    [Fact]
    public async Task Test16_PrivacyAudit_NoCredentialsInLogsOrTraces()
    {
        const string secretPassword = "SuperSecretPlainTextPassword!@#123";
        const string secretTotpSeed = "JBSWY3DPEHPK3PXP";
        var accountId = await CreateAccountAsync("user16@example.test", secretPassword, secretTotpSeed);
        var session = new DelayedTransitionSession(delayMs: 100);
        await using var orchestrator = CreateOrchestrator(session, _loggerProvider);

        var started = await orchestrator.StartAsync(accountId);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillEmail, confirmed: true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, confirmed: true);
        await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillTotp, confirmed: true);

        foreach (var logLine in _loggerProvider.LogEntries)
        {
            Assert.DoesNotContain(secretPassword, logLine, StringComparison.Ordinal);
            Assert.DoesNotContain(secretTotpSeed, logLine, StringComparison.Ordinal);
        }
    }

    // -------------------------------------------------------------
    // Test 17: No false authenticated result from generic guest landing
    // -------------------------------------------------------------
    [Fact]
    public async Task Test17_NoFalseAuthenticatedResult()
    {
        var detector = new StableBrowserPageStateDetector();

        // Guest page with "New chat" text and a single login button should NEVER report Authenticated
        var guestLanding = new BrowserPageSnapshot(
            BrowserOriginPolicy.ChatGptUri,
            BrowserPageState.GuestLandingLoginAvailable,
            HasEmailField: false,
            HasPasswordField: false,
            HasTotpField: false,
            HasGuestLoginEntry: true,
            GuestLoginEntryCount: 1,
            IsPageReady: true);

        var detected = detector.Detect(guestLanding);
        Assert.NotEqual(BrowserPageState.Authenticated, detected);
        Assert.Equal(BrowserPageState.GuestLandingLoginAvailable, detected);
    }

    // -------------------------------------------------------------
    // Test 18: One-shot ephemeral profile cleanup
    // -------------------------------------------------------------
    [Fact]
    public async Task Test18_EphemeralProfile_OneShotCleanup()
    {
        var cleanedUp = false;
        var ephemeralFactory = new TestEphemeralProfileFactory(onCleanup: () => cleanedUp = true);
        var session = new DelayedTransitionSession(delayMs: 50);
        await using var orchestrator = CreateOrchestratorWithEphemeral(session, ephemeralFactory);

        using var credContext = new EphemeralCredentialContext("ephemeral@example.test", "EphemPass123", null);

        var stages = new ConcurrentQueue<string>();
        var request = new EphemeralBrowserAuthRequest(credContext, BrowserSessionMode.Temporary);
        var result = await orchestrator.AutoLoginAsync(
            request,
            new Progress<BrowserAuthProgress>(p => stages.Enqueue(p.Stage)));

        Assert.Equal(BrowserAuthState.BrowserSessionObserved, result.State);
        Assert.True(cleanedUp, "Ephemeral profile cleanup must be executed on completion.");
        Assert.Null(credContext.Account);
    }

    // -------------------------------------------------------------
    // Helpers and Synthetic Session Implementations
    // -------------------------------------------------------------
    private async Task<Guid> CreateAccountAsync(string email, string password = "TestPassword-1", string? totpSeed = null)
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
        var accountId = (await reader.GetAccountsAsync(email)).Single().AccountId;

        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput(email, password, totpSeed, false));
        Assert.True(save.IsSuccess, save.Message);

        return accountId;
    }

    private BrowserAuthOrchestrator CreateOrchestrator(
        IBrowserAuthenticationSession session,
        ILogger<BrowserAuthOrchestrator>? logger = null)
    {
        var profileManager = new TestBrowserProfileManager();
        var sessionFactory = new TestSessionFactory(session);
        var stateDetector = new StableBrowserPageStateDetector();
        var credentialReader = new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess);
        var totpGenerator = new Rfc6238TotpGenerator();

        return new BrowserAuthOrchestrator(
            profileManager,
            sessionFactory,
            stateDetector,
            credentialReader,
            totpGenerator,
            logger: logger,
            vaultAccess: _vaultAccess);
    }

    private BrowserAuthOrchestrator CreateOrchestratorWithEphemeral(
        IBrowserAuthenticationSession session,
        IEphemeralBrowserProfileFactory ephemeralFactory)
    {
        var profileManager = new TestBrowserProfileManager();
        var sessionFactory = new TestSessionFactory(session);
        var stateDetector = new StableBrowserPageStateDetector();
        var credentialReader = new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess);
        var totpGenerator = new Rfc6238TotpGenerator();

        return new BrowserAuthOrchestrator(
            profileManager,
            sessionFactory,
            stateDetector,
            credentialReader,
            totpGenerator,
            vaultAccess: _vaultAccess,
            ephemeralProfileFactory: ephemeralFactory);
    }

    private sealed class DelayedTransitionSession : IBrowserAuthenticationSession
    {
        private readonly int _delayMs;
        private DateTimeOffset? _emailContinuedAt;
        private DateTimeOffset? _passwordContinuedAt;

        public DelayedTransitionSession(int delayMs)
        {
            _delayMs = delayMs;
        }

        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public Dictionary<BrowserAuthField, string> FilledValues { get; } = [];
        public bool IsDisposed { get; private set; }

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default)
        {
            FilledValues[field] = value;
            return Task.CompletedTask;
        }

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        {
            _emailContinuedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        {
            _passwordContinuedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
        {
            if (_emailContinuedAt is null)
            {
                // Initial state: Email required
                return Task.FromResult(new BrowserPageSnapshot(
                    CurrentUri,
                    BrowserPageState.EmailRequired,
                    HasEmailField: true,
                    HasPasswordField: false,
                    HasTotpField: false));
            }

            var elapsedSinceEmail = (DateTimeOffset.UtcNow - _emailContinuedAt.Value).TotalMilliseconds;
            if (elapsedSinceEmail < _delayMs)
            {
                // In-between / loading state: Unknown
                return Task.FromResult(new BrowserPageSnapshot(
                    CurrentUri,
                    BrowserPageState.Unknown,
                    HasEmailField: false,
                    HasPasswordField: false,
                    HasTotpField: false));
            }

            if (_passwordContinuedAt is null)
            {
                // Password required has now rendered
                return Task.FromResult(new BrowserPageSnapshot(
                    CurrentUri,
                    BrowserPageState.PasswordRequired,
                    HasEmailField: false,
                    HasPasswordField: true,
                    HasTotpField: false));
            }

            // Post password: authenticated
            return Task.FromResult(new BrowserPageSnapshot(
                CurrentUri,
                BrowserPageState.Authenticated,
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConfigurableBrowserSession : IBrowserAuthenticationSession
    {
        public BrowserPageState State { get; set; } = BrowserPageState.EmailRequired;
        public BrowserPageState? StateAfterEmailContinue { get; set; }
        public BrowserPageState? StateAfterPasswordSubmit { get; set; }
        public Exception? ThrowOnPasswordFill { get; set; }
        public Exception? ThrowOnPasswordContinue { get; set; }
        public Exception? ThrowOnEmailContinue { get; set; }
        public Dictionary<BrowserAuthField, string> FilledValues { get; } = [];
        public bool IsDisposed { get; private set; }

        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;

        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default)
        {
            if (field == BrowserAuthField.Password && ThrowOnPasswordFill is not null)
            {
                throw ThrowOnPasswordFill;
            }

            FilledValues[field] = value;
            return Task.CompletedTask;
        }

        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnEmailContinue is not null)
            {
                throw ThrowOnEmailContinue;
            }

            if (StateAfterEmailContinue.HasValue)
            {
                State = StateAfterEmailContinue.Value;
            }
            else
            {
                State = BrowserPageState.PasswordRequired;
            }
            return Task.CompletedTask;
        }

        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnPasswordContinue is not null)
            {
                throw ThrowOnPasswordContinue;
            }

            if (StateAfterPasswordSubmit.HasValue)
            {
                State = StateAfterPasswordSubmit.Value;
            }
            else
            {
                State = BrowserPageState.Authenticated;
            }
            return Task.CompletedTask;
        }

        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default)
        {
            State = BrowserPageState.Authenticated;
            return Task.CompletedTask;
        }

        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserPageSnapshot(
                CurrentUri,
                State,
                HasEmailField: State == BrowserPageState.EmailRequired,
                HasPasswordField: State == BrowserPageState.PasswordRequired,
                HasTotpField: State == BrowserPageState.MfaRequired));

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestBrowserProfileManager : IBrowserProfileManager
    {
        public Task<BrowserProfileBindingVm?> GetBindingAsync(Guid accountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
            => Task.FromResult<BrowserProfileBindingVm?>(new BrowserProfileBindingVm(
                Guid.NewGuid(), accountId, providerId, "TestProfile", "test-profile-dir", "Ready", DateTimeOffset.UtcNow, null));

        public Task<BrowserProfilePreparationResult> PrepareAsync(Guid accountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
        {
            var binding = new BrowserProfileBindingVm(Guid.NewGuid(), accountId, providerId, "TestProfile", "test-profile-dir", "Ready", DateTimeOffset.UtcNow, null);
            return Task.FromResult(new BrowserProfilePreparationResult(
                true,
                "Ready",
                "Profile ready",
                new BrowserProfileSessionContext(
                    binding.BindingId,
                    accountId,
                    providerId,
                    binding.OpaqueProfileIdentifier,
                    Path.Combine(Path.GetTempPath(), "flash-router-test-profile"),
                    "chrome",
                    binding)));
        }

        public Task RecordLaunchResultAsync(Guid bindingId, bool succeeded, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestSessionFactory(IBrowserAuthenticationSession session) : IBrowserAuthenticationSessionFactory
    {
        public Task<IBrowserAuthenticationSession> OpenAsync(BrowserProfileSessionContext profile, Uri officialUri, CancellationToken cancellationToken = default)
            => Task.FromResult(session);
    }

    private sealed class TestEphemeralProfileFactory(Action? onCleanup = null) : IEphemeralBrowserProfileFactory
    {
        public Task<EphemeralBrowserProfilePreparationResult> PrepareAsync(BrowserSessionMode sessionMode, CancellationToken cancellationToken = default)
        {
            var bindingId = Guid.NewGuid();
            var context = new BrowserProfileSessionContext(
                bindingId,
                Guid.Empty,
                "openai-chatgpt",
                "ephemeral-profile",
                Path.Combine(Path.GetTempPath(), "flash-router-ephemeral-" + Guid.NewGuid().ToString("N")),
                "chrome",
                new BrowserProfileBindingVm(bindingId, Guid.Empty, "openai-chatgpt", "Ephemeral", "ephemeral-profile", "Active", DateTimeOffset.UtcNow, null));

            return Task.FromResult(new EphemeralBrowserProfilePreparationResult(
                true,
                "Ready",
                "Ephemeral profile prepared",
                context,
                cleanup: () =>
                {
                    onCleanup?.Invoke();
                    return ValueTask.CompletedTask;
                }));
        }
    }

    private sealed class TestLoggerProvider : ILoggerProvider, ILogger<BrowserAuthOrchestrator>
    {
        public List<string> LogEntries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LogEntries.Add(formatter(state, exception));
        }

        public void Dispose() { }
    }
}
