using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class AccountBrowserSessionManagerTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task OpenCloseAndReopenAreAccountScopedAndIdempotent()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var profiles = new FakeProfileManager(accountA, accountB);
        var launcher = new FakeChromeLauncher();
        using var manager = new AccountBrowserSessionManager(profiles, launcher);

        var openedA = await manager.OpenAsync(accountA);
        var openedB = await manager.OpenAsync(accountB);

        Assert.True(openedA.IsSuccess);
        Assert.True(openedB.IsSuccess);
        Assert.Equal(AccountBrowserRuntimeState.Open, (await manager.GetStatusAsync(accountA)).RuntimeState);
        Assert.Equal(AccountBrowserRuntimeState.Open, (await manager.GetStatusAsync(accountB)).RuntimeState);

        var closedA = await manager.CloseAsync(accountA);
        var closedAgainA = await manager.CloseAsync(accountA);

        Assert.True(closedA.IsSuccess);
        Assert.Equal(AccountBrowserRuntimeState.Closed, closedA.Snapshot.RuntimeState);
        Assert.Equal(BrowserCloseReason.UserRequested, closedA.Snapshot.CloseReason);
        Assert.Equal("ALREADY_CLOSED", closedAgainA.Status);
        Assert.True(launcher.IsRunning(profiles.Contexts[accountB].ProfileDirectory));
        Assert.False(launcher.IsRunning(profiles.Contexts[accountA].ProfileDirectory));

        var reopenedA = await manager.ReopenAsync(accountA);
        Assert.True(reopenedA.IsSuccess);
        Assert.Equal(AccountBrowserRuntimeState.Open, reopenedA.Snapshot.RuntimeState);
        Assert.True(launcher.IsRunning(profiles.Contexts[accountA].ProfileDirectory));
    }

    [Fact]
    public async Task SessionCheckDoesNotPromoteOpenBrowserToAuthenticatedSession()
    {
        var accountId = Guid.NewGuid();
        var profiles = new FakeProfileManager(accountId);
        using var manager = new AccountBrowserSessionManager(profiles, new FakeChromeLauncher());

        await manager.OpenAsync(accountId);
        var result = await manager.CheckSessionAsync(accountId);

        Assert.False(result.IsSuccess);
        Assert.Equal("SESSION_OBSERVATION_UNAVAILABLE", result.Status);
        Assert.Equal(AuthenticationObservationStatus.Unknown, result.Snapshot.AuthenticationState);
        Assert.Equal(AccountBrowserRuntimeState.Open, result.Snapshot.RuntimeState);
    }

    [Fact]
    public async Task SessionCheckPreservesVerifiedIdentityForTheSameBrowserAttempt()
    {
        var accountId = await CreateAccountAsync("same-session-identity@example.test");
        var attemptId = Guid.NewGuid();
        var profiles = new FakeProfileManager(accountId);
        var launcher = new FakeChromeLauncher();
        var scopedServices = new ServiceCollection();
        scopedServices.AddScoped(_ => new SqliteAccountAuthenticationStateStore(_database.ContextFactory));
        scopedServices.AddScoped<IAccountAuthenticationStateStore>(provider => provider.GetRequiredService<SqliteAccountAuthenticationStateStore>());
        scopedServices.AddScoped<IAccountAuthenticationStateReader>(provider => provider.GetRequiredService<SqliteAccountAuthenticationStateStore>());
        await using var scopeProvider = scopedServices.BuildServiceProvider();
        using var manager = new AccountBrowserSessionManager(
            profiles,
            launcher,
            scopeProvider.GetRequiredService<IServiceScopeFactory>());

        await manager.OpenAsync(accountId);
        await manager.RegisterManagedSessionAsync(
            accountId,
            attemptId,
            profiles.Contexts[accountId],
            new AuthenticatedSession());
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await new SqliteAccountAuthenticationStateStore(_database.ContextFactory).RecordAsync(
            new AccountAuthenticationStateUpdate(
                accountId,
                attemptId,
                "openai-chatgpt",
                "AuthenticatedSessionConfirmed",
                "IdentityVerified",
                startedAt,
                IdentityVerifiedAtUtc: startedAt,
                ObservedAtUtc: startedAt,
                EvidenceSource: "synthetic matching account evidence"));

        var result = await manager.CheckSessionAsync(accountId);
        var persisted = await new SqliteAccountAuthenticationStateStore(_database.ContextFactory).GetAsync(accountId);

        Assert.True(result.IsSuccess);
        Assert.Equal(IdentityVerificationStatus.IdentityVerified, result.Snapshot.IdentityVerificationState);
        Assert.NotNull(persisted);
        Assert.Equal("IdentityVerified", persisted!.IdentityVerificationState);
        Assert.Equal(attemptId, persisted.AttemptId);
    }

    [Fact]
    public async Task StaleAttemptCannotOverwriteOrReleaseNewerManagedSession()
    {
        var accountId = Guid.NewGuid();
        var profiles = new FakeProfileManager(accountId);
        using var manager = new AccountBrowserSessionManager(profiles, new FakeChromeLauncher());
        var first = new NoopSession();
        var second = new NoopSession();
        var profile = profiles.Contexts[accountId];
        var firstAttempt = Guid.NewGuid();
        var secondAttempt = Guid.NewGuid();

        await manager.RegisterManagedSessionAsync(accountId, firstAttempt, profile, first);
        await Assert.ThrowsAsync<BrowserAuthSessionException>(() =>
            manager.RegisterManagedSessionAsync(accountId, secondAttempt, profile, second));
        await manager.UnregisterManagedSessionAsync(accountId, secondAttempt);
        var status = await manager.GetStatusAsync(accountId);

        Assert.Equal(firstAttempt, status.ActiveAttemptId);
        Assert.Equal(AccountBrowserRuntimeState.Open, status.RuntimeState);
        Assert.False(first.IsDisposed);
    }

    [Fact]
    public async Task RuntimeSnapshotUsesBindingOwnedByActiveManagedSession()
    {
        var accountId = Guid.NewGuid();
        var profiles = new FakeProfileManager(accountId);
        using var manager = new AccountBrowserSessionManager(profiles, new FakeChromeLauncher());
        var candidateBinding = Guid.NewGuid();
        var candidate = profiles.Contexts[accountId] with
        {
            BindingId = candidateBinding,
            Binding = profiles.Contexts[accountId].Binding with { BindingId = candidateBinding }
        };

        await manager.RegisterManagedSessionAsync(accountId, Guid.NewGuid(), candidate, new NoopSession());

        var status = await manager.GetStatusAsync(accountId);

        Assert.Equal(candidateBinding, status.BrowserProfileBindingId);
    }

    [Fact]
    public async Task AccountObservationWithoutRuntimeReturnsActionableBrowserNotOpen()
    {
        var accountId = Guid.NewGuid();
        var profiles = new FakeProfileManager(accountId);
        using var manager = new AccountBrowserSessionManager(profiles, new FakeChromeLauncher());

        var result = await manager.ObserveAccountDataAsync(
            accountId,
            AccountObservationPersistenceMode.DoNotPersist);

        Assert.False(result.IsSuccess);
        Assert.Equal("BROWSER_NOT_OPEN", result.Status);
        Assert.Contains("Chưa có phiên trình duyệt đang mở", result.Message, StringComparison.Ordinal);
    }

    private sealed class FakeChromeLauncher : IChromeProcessLauncher, IChromeProcessLauncherDiagnostics, IChromeProcessController
    {
        private readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);

        public bool TryLaunch(string executablePath, string profileDirectory, string officialUrl, out string? safeError)
        {
            _running.Add(profileDirectory);
            safeError = null;
            return true;
        }

        public ChromeLaunchResult TryLaunchDetailed(string executablePath, string profileDirectory, string officialUrl)
        {
            _running.Add(profileDirectory);
            return new ChromeLaunchResult(true, "BROWSER_OPENED", "synthetic");
        }

        public bool IsRunning(string profileDirectory) => _running.Contains(profileDirectory);

        public bool TryClose(string profileDirectory, out string? safeError)
        {
            _running.Remove(profileDirectory);
            safeError = null;
            return true;
        }
    }

    private sealed class FakeProfileManager : IBrowserProfileManager
    {
        public FakeProfileManager(params Guid[] accountIds)
        {
            foreach (var accountId in accountIds)
            {
                var binding = new BrowserProfileBindingVm(
                    Guid.NewGuid(),
                    accountId,
                    "openai-chatgpt",
                    "Chrome",
                    "synthetic-" + accountId.ToString("N"),
                    "Ready",
                    DateTimeOffset.UtcNow,
                    null);
                Contexts[accountId] = new BrowserProfileSessionContext(
                    binding.BindingId,
                    accountId,
                    binding.ProviderId,
                    binding.OpaqueProfileIdentifier,
                    Path.Combine(Path.GetTempPath(), "flash-router-" + accountId.ToString("N")),
                    "synthetic-chrome.exe",
                    binding);
            }
        }

        public Dictionary<Guid, BrowserProfileSessionContext> Contexts { get; } = [];

        public Task<BrowserProfileBindingVm?> GetBindingAsync(Guid accountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
            => Task.FromResult(Contexts.TryGetValue(accountId, out var context) ? context.Binding : null);

        public Task<BrowserProfilePreparationResult> PrepareAsync(Guid accountId, string providerId = "openai-chatgpt", CancellationToken cancellationToken = default)
            => Task.FromResult(Contexts.TryGetValue(accountId, out var context)
                ? new BrowserProfilePreparationResult(true, "BrowserProfileReady", "synthetic", context)
                : new BrowserProfilePreparationResult(false, "ACCOUNT_NOT_FOUND", "missing"));

        public Task RecordLaunchResultAsync(Guid bindingId, bool succeeded, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoopSession : IBrowserAuthenticationSession
    {
        public bool IsDisposed { get; private set; }
        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserPageSnapshot(CurrentUri, BrowserPageState.Unknown));
        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private async Task<Guid> CreateAccountAsync(string email)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = "Session projection test",
            Provider = "openai-chatgpt",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(result.IsSuccess, result.Message);
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email)).Single().AccountId;
    }

    private sealed class AuthenticatedSession : IBrowserAuthenticationSession
    {
        public Uri CurrentUri => BrowserOriginPolicy.ChatGptUri;
        public Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateLoginEntryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FillAsync(BrowserAuthField field, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateEmailContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivatePasswordContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ActivateTotpContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BrowserPageSnapshot> ReadPageAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new BrowserPageSnapshot(CurrentUri, BrowserPageState.Authenticated));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
