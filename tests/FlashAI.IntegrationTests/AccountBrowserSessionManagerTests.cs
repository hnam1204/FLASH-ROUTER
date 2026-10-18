using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.IntegrationTests;

public sealed class AccountBrowserSessionManagerTests
{
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
}
