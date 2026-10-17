using FlashAI.Foundation.Security;
using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class AuthBrowser11Tests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();
    private readonly string _vaultRoot = Path.Combine(Path.GetTempPath(), "flash-router-auth-browser-11-" + Guid.NewGuid().ToString("N"));
    private IDataProtectionProvider _dataProtection = null!;
    private ISecretVault _vault = null!;
    private VaultAccessState _vaultAccess = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _dataProtection = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_vaultRoot, "keys")),
            builder => builder.SetApplicationName("FLASH ROUTER AUTH BROWSER 11 TEST"));
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
    public async Task ReadinessDistinguishesStoredReferenceFromLockedVault()
    {
        var accountId = await CreateAccountAsync("auth11-readiness@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var saved = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("auth11-readiness@example.test", "SyntheticReplacement-1", "JBSWY3DPEHPK3PXP", false));
        Assert.True(saved.IsSuccess, saved.Message);

        var reader = new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess);
        var locked = await reader.CheckReadinessAsync(accountId);
        Assert.Equal(BrowserAuthCredentialReadinessKind.VaultLocked, locked.Kind);
        Assert.False(locked.IsReady);
        Assert.NotNull(locked.ConnectionId);
        Assert.NotNull(locked.CredentialReferenceId);

        _vaultAccess.SetUnlocked(true);
        var ready = await reader.CheckReadinessAsync(accountId);
        Assert.Equal(BrowserAuthCredentialReadinessKind.CredentialReady, ready.Kind);
        Assert.True(ready.IsReady);
        Assert.Equal(locked.ConnectionId, ready.ConnectionId);
        Assert.Equal(locked.CredentialReferenceId, ready.CredentialReferenceId);
    }

    [Fact]
    public async Task ReplaceCredentialPreservesConnectionAndReferenceIdentity()
    {
        var accountId = await CreateAccountAsync("auth11-replace@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var saved = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("auth11-replace@example.test", "SyntheticOldReplacement-1", null, false));
        Assert.True(saved.IsSuccess, saved.Message);
        _vaultAccess.SetUnlocked(true);

        var reader = new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess);
        var before = await reader.GetReferenceSnapshotAsync(accountId);
        var replaced = await credentialPort.ReplaceAsync(
            accountId,
            new ChatGptCredentialInput("auth11-replace@example.test", "SyntheticNewReplacement-2", "JBSWY3DPEHPK3PXP", false));

        Assert.True(replaced.IsSuccess, replaced.Message);
        Assert.Equal(before.ConnectionId, replaced.ConnectionId);
        var after = await reader.GetReferenceSnapshotAsync(accountId);
        Assert.Equal(before.ConnectionId, after.ConnectionId);
        Assert.Equal(before.CredentialReferenceId, after.CredentialReferenceId);

        var stored = await reader.ReadAsync(accountId);
        Assert.True(stored.IsSuccess, stored.Message);
        Assert.Equal("SyntheticNewReplacement-2", stored.Credential!.Password);
    }

    [Fact]
    public async Task HistoricalLaunchFailedRemainsRetryableWhenCredentialsAreReady()
    {
        var accountId = await CreateAccountAsync("auth11-launchfailed@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var saved = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("auth11-launchfailed@example.test", "SyntheticLaunchFailed-1", null, false));
        Assert.True(saved.IsSuccess, saved.Message);
        _vaultAccess.SetUnlocked(true);

        await using var orchestrator = new BrowserAuthOrchestrator(
            new HistoricalFailureProfileManager(accountId),
            new NoopSessionFactory(),
            new StableBrowserPageStateDetector(),
            new BrowserAuthCredentialReader(_database.ContextFactory, _vault, _vaultAccess),
            new Rfc6238TotpGenerator());

        var preflight = await orchestrator.GetAutoLoginPreflightAsync(accountId);

        Assert.True(preflight.IsReady, preflight.Message);
        Assert.True(preflight.BrowserProfileRetryAvailable);
        Assert.Equal("BrowserLaunchFailed", preflight.BrowserProfileFailureStatus);
        Assert.Contains("lịch sử", preflight.BrowserProfileStatus, StringComparison.OrdinalIgnoreCase);
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

    private sealed class HistoricalFailureProfileManager(Guid accountId) : IBrowserProfileManager
    {
        public Task<BrowserProfileBindingVm?> GetBindingAsync(Guid requestedAccountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
            => Task.FromResult<BrowserProfileBindingVm?>(new BrowserProfileBindingVm(
                Guid.NewGuid(),
                accountId,
                providerId,
                "Chrome",
                "historical-failure",
                "LaunchFailed",
                DateTimeOffset.UtcNow,
                null));

        public Task<BrowserProfilePreparationResult> PrepareAsync(Guid requestedAccountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserProfilePreparationResult(false, "BrowserLaunchFailed", "Synthetic historical failure."));

        public Task RecordLaunchResultAsync(Guid bindingId, bool succeeded, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoopSessionFactory : IBrowserAuthenticationSessionFactory
    {
        public Task<IBrowserAuthenticationSession> OpenAsync(BrowserProfileSessionContext profile, Uri officialUri, CancellationToken cancellationToken = default)
            => Task.FromException<IBrowserAuthenticationSession>(new BrowserAuthSessionException("NOT_USED", "Synthetic session is not used by this preflight test."));
    }
}
