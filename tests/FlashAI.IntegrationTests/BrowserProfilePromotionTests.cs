using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class BrowserProfilePromotionTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task UnverifiedCandidateIsBlockedAndVerifiedPromotionSurvivesManagerRestart()
    {
        var accountId = await CreateAccountAsync("promotion@example.test");
        var paths = new LocalDataPaths(
            _database.Root,
            Path.Combine(_database.Root, ".local", "automation-profiles"));
        var originalDirectory = Path.Combine(_database.Root, ".local", "browser-profiles");
        var candidateDirectory = Path.Combine(paths.AutomationBrowserProfiles, "candidate-profile");
        Directory.CreateDirectory(candidateDirectory);

        BrowserProfileBindingVm original;
        using (var manager = new BrowserProfileManager(
                   _database.ContextFactory,
                   paths,
                   new FixedChromeResolver()))
        {
            var prepared = await manager.PrepareAsync(accountId);
            Assert.True(prepared.IsSuccess, prepared.Message);
            original = prepared.Context!.Binding;
            Assert.StartsWith(originalDirectory, prepared.Context.ProfileDirectory, StringComparison.OrdinalIgnoreCase);

            var candidateId = Guid.NewGuid();
            var candidate = new BrowserProfileSessionContext(
                candidateId,
                accountId,
                "openai-chatgpt",
                "candidate-profile",
                candidateDirectory,
                "synthetic-chrome",
                new BrowserProfileBindingVm(
                    candidateId,
                    accountId,
                    "openai-chatgpt",
                    "Chrome",
                    "candidate-profile",
                    "Available",
                    DateTimeOffset.UtcNow,
                    null));

            var blocked = await manager.PromoteCandidateAsync(
                accountId,
                candidate,
                IdentityVerificationStatus.Unverified);
            Assert.False(blocked.IsSuccess);
            Assert.Equal("PROFILE_PROMOTION_IDENTITY_NOT_VERIFIED", blocked.Status);
            Assert.Equal(original.BindingId, (await manager.GetBindingAsync(accountId))!.BindingId);

            var promoted = await manager.PromoteCandidateAsync(
                accountId,
                candidate,
                IdentityVerificationStatus.IdentityVerified);
            Assert.True(promoted.IsSuccess, promoted.Message);
            Assert.Equal(candidateId, promoted.Binding!.BindingId);
            Assert.True(Directory.Exists(prepared.Context.ProfileDirectory));
        }

        using (var restartedManager = new BrowserProfileManager(
                   _database.ContextFactory,
                   paths,
                   new FixedChromeResolver()))
        {
            var authoritative = await restartedManager.GetBindingAsync(accountId);
            Assert.NotNull(authoritative);
            Assert.NotEqual(original.BindingId, authoritative!.BindingId);
            Assert.True(Directory.Exists(candidateDirectory));
        }
    }

    private async Task<Guid> CreateAccountAsync(string email)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var created = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = "Promotion test",
            Provider = "openai-chatgpt",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(created.IsSuccess, created.Message);

        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email)).Single().AccountId;
    }

    private sealed class FixedChromeResolver : IChromeExecutableResolver
    {
        public string? Resolve() => "synthetic-chrome";
    }
}
