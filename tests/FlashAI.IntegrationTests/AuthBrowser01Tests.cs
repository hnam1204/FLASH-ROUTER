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

        var locked = await orchestrator.ConfirmAndFillAsync(started.AttemptId.Value, BrowserAuthAction.FillPassword, confirmed: true);
        Assert.Equal("VAULT_LOCKED", locked.Status);
        Assert.False(session.FilledValues.ContainsKey(BrowserAuthField.Password));

        _vaultAccess.SetUnlocked(true);
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
        _vaultAccess.SetUnlocked(true);

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
    public async Task AutoLoginExposesBoundedRedactedStageTimingAndLeavesUnexecutedStagesUnset()
    {
        var accountId = await CreateAccountAsync("synthetic-timing@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-timing@example.test", "SyntheticTimingPassword-123", "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);
        _vaultAccess.SetUnlocked(true);

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
    public async Task AutoLoginPreflightRequiresUnlockedVaultAndCredentialReference()
    {
        var accountId = await CreateAccountAsync("synthetic-preflight@example.test");
        await using var orchestrator = CreateOrchestrator(new SyntheticBrowserSession());

        var locked = await orchestrator.GetAutoLoginPreflightAsync(accountId);
        Assert.False(locked.IsReady);
        Assert.Equal("VAULT_LOCKED", locked.Status);

        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("synthetic-preflight@example.test", "SyntheticPreflightPassword-123", null, false));
        Assert.True(save.IsSuccess, save.Message);
        _vaultAccess.SetUnlocked(true);

        var ready = await orchestrator.GetAutoLoginPreflightAsync(accountId);
        Assert.True(ready.IsReady, ready.Message);
        Assert.True(ready.CredentialReferenceReady);
        Assert.Equal("https://chatgpt.com/", ready.OfficialDestination);
    }

    [Fact]
    public async Task AutoLoginStopsAtVaultGateBeforePasswordFill()
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

        Assert.Equal(BrowserAuthState.PasswordRequired, result.State);
        Assert.Equal("VAULT_LOCKED", result.Status);
        Assert.Equal("synthetic-vault-gate@example.test", session.FilledValues[BrowserAuthField.Email]);
        Assert.False(session.FilledValues.ContainsKey(BrowserAuthField.Password));
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
        _vaultAccess.SetUnlocked(true);

        var session = new SyntheticBrowserSession { State = BrowserPageState.PasswordRequired };
        await using var orchestrator = CreateOrchestrator(session);
        var started = await orchestrator.StartAsync(other);
        var result = await orchestrator.ConfirmAndFillAsync(started.AttemptId!.Value, BrowserAuthAction.FillPassword, confirmed: true);

        Assert.Equal("CREDENTIAL_NOT_FOUND", result.Status);
        Assert.False(session.FilledValues.ContainsKey(BrowserAuthField.Password));
    }

    private BrowserAuthOrchestrator CreateOrchestrator(
        SyntheticBrowserSession session,
        IBrowserAuthenticationSessionFactory? factory = null,
        IBrowserProfileManager? profileManager = null)
    {
        return new BrowserAuthOrchestrator(
            profileManager ?? new SyntheticProfileManager(),
            factory ?? new SyntheticSessionFactory(session),
            new StableBrowserPageStateDetector(),
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            new Rfc6238TotpGenerator());
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
