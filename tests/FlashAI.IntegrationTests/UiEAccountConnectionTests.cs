using System.Text;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class CredentialLineCodecTests
{
    [Fact]
    public void ParsesSimpleCredentialWithoutTotp()
    {
        var parsed = CredentialLineCodec.TryParse(
            "sample@example.test|ExamplePassword123|",
            out var credential,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("sample@example.test", credential!.Account);
        Assert.Equal("ExamplePassword123", credential.Password);
        Assert.Null(credential.TotpSeed);
    }

    [Fact]
    public void QuotedDelimiterRoundTripsWithoutAmbiguity()
    {
        var original = new CredentialLine(
            "sample@example.test",
            "pass|word\\with\"quotes",
            "JBSWY3DPEHPK3PXP");

        var serialized = CredentialLineCodec.Serialize(original);
        var parsed = CredentialLineCodec.TryParse(serialized, out var credential, out var error);

        Assert.True(parsed, error);
        Assert.Equal(original, credential);
    }

    [Theory]
    [InlineData("sample@example.test|ExampleSecret123")]
    [InlineData("sample@example.test|password|123456")]
    [InlineData("sample@example.test|pass|word|")]
    public void RejectsAmbiguousOrTemporaryOtpInputWithoutEchoingSecrets(string input)
    {
        var parsed = CredentialLineCodec.TryParse(input, out _, out var error);

        Assert.False(parsed);
        Assert.NotEmpty(error);
        Assert.DoesNotContain(input, error, StringComparison.Ordinal);
    }
}

public sealed class SecureAccountCopyTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();
    private readonly string _vaultRoot = Path.Combine(Path.GetTempPath(), "flash-router-ui-e-copy-" + Guid.NewGuid().ToString("N"));
    private IDataProtectionProvider _dataProtection = null!;
    private ISecretVault _vault = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _dataProtection = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(_vaultRoot, "keys")),
            builder => builder.SetApplicationName("FLASH ROUTER UI-E COPY TEST"));
        _vault = new FileSecretVault(Path.Combine(_vaultRoot, "vault"), _dataProtection);
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
    public async Task BasicCopyExcludesSecretsAndCredentialCopyRequiresConfirmationButNotVaultUnlock()
    {
        var accountId = await CreateAccountAsync("copy@example.test");
        var password = "copy|password-123";
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            accountId,
            new ChatGptCredentialInput("copy@example.test", password, "JBSWY3DPEHPK3PXP", false));
        Assert.True(save.IsSuccess, save.Message);

        var access = new VaultAccessState();
        var copyPort = new AccountCopyUiPort(_database.ContextFactory, _vault, access);

        var basic = await copyPort.CopyBasicAsync(accountId);
        Assert.True(basic.IsSuccess, basic.Message);
        Assert.False(basic.ContainsSecret);
        Assert.DoesNotContain(password, basic.ClipboardText, StringComparison.Ordinal);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", basic.ClipboardText, StringComparison.Ordinal);

        var unconfirmed = await copyPort.CopySavedCredentialsAsync(accountId, confirmed: false);
        Assert.Equal("COPY_CONFIRMATION_REQUIRED", unconfirmed.Status);

        Assert.False(access.IsUnlocked);
        var copied = await copyPort.CopySavedCredentialsAsync(accountId, confirmed: true);
        Assert.True(copied.IsSuccess, copied.Message);
        Assert.True(copied.ContainsSecret);
        Assert.Equal(
            "copy@example.test|\"copy|password-123\"|JBSWY3DPEHPK3PXP",
            copied.ClipboardText);
    }

    [Fact]
    public async Task CredentialCopyCannotCrossAccountOwnership()
    {
        var owner = await CreateAccountAsync("owner@example.test");
        var other = await CreateAccountAsync("other@example.test");
        var credentialPort = new SqliteAccountCredentialUiPort(_database.ContextFactory, _vault);
        var save = await credentialPort.SaveAsync(
            owner,
            new ChatGptCredentialInput("owner@example.test", "owner-secret", null, false));
        Assert.True(save.IsSuccess, save.Message);

        var access = new VaultAccessState();
        access.SetUnlocked(true);
        var copyPort = new AccountCopyUiPort(_database.ContextFactory, _vault, access);

        var result = await copyPort.CopySavedCredentialsAsync(other, confirmed: true);

        Assert.Equal("CREDENTIAL_NOT_FOUND", result.Status);
        Assert.DoesNotContain("owner-secret", result.Message, StringComparison.Ordinal);
    }

    private async Task<Guid> CreateAccountAsync(string email)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = email.Split('@')[0],
            Provider = "openai",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(result.IsSuccess, result.Message);

        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email)).Single().AccountId;
    }
}

public sealed class BrowserProfileUiPortTests : IAsyncLifetime
{
    private readonly SqliteAccountDatabaseFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AccountsGetDistinctPersistentChromeProfiles()
    {
        var firstAccount = await CreateAccountAsync("browser-a@example.test");
        var secondAccount = await CreateAccountAsync("browser-b@example.test");
        var launcher = new CapturingChromeLauncher { ShouldSucceed = true };
        using var port = CreatePort(launcher);

        var firstOpen = await port.OpenAsync(firstAccount, BrowserOpenTarget.ChatGpt);
        var secondOpen = await port.OpenAsync(secondAccount, BrowserOpenTarget.ChatGpt);

        Assert.True(firstOpen.IsSuccess, firstOpen.Message);
        Assert.True(secondOpen.IsSuccess, secondOpen.Message);
        Assert.NotNull(firstOpen.Binding);
        Assert.NotNull(secondOpen.Binding);
        Assert.NotEqual(firstOpen.Binding!.OpaqueProfileIdentifier, secondOpen.Binding!.OpaqueProfileIdentifier);
        Assert.Equal(2, launcher.ProfileDirectories.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(launcher.OfficialUrls, url => Assert.Equal("https://chatgpt.com/", url));

        var firstDirectory = Path.Combine(_database.Root, ".local", "browser-profiles", firstOpen.Binding.OpaqueProfileIdentifier);
        var secondDirectory = Path.Combine(_database.Root, ".local", "browser-profiles", secondOpen.Binding.OpaqueProfileIdentifier);
        Assert.True(Directory.Exists(firstDirectory));
        Assert.True(Directory.Exists(secondDirectory));

        using var restartedPort = CreatePort(launcher);
        var persisted = await restartedPort.GetBindingAsync(firstAccount);
        Assert.NotNull(persisted);
        Assert.Equal(firstOpen.Binding.BindingId, persisted!.BindingId);
        Assert.Equal(firstOpen.Binding.OpaqueProfileIdentifier, persisted.OpaqueProfileIdentifier);

        var reopened = await restartedPort.OpenAsync(firstAccount, BrowserOpenTarget.ChatGpt);
        Assert.True(reopened.IsSuccess, reopened.Message);
        Assert.Equal(firstOpen.Binding.BindingId, reopened.Binding?.BindingId);
    }

    [Fact]
    public async Task MissingChromeDoesNotCreateAProfile()
    {
        var accountId = await CreateAccountAsync("missing-browser@example.test");
        using var port = CreatePort(new CapturingChromeLauncher(), chromePath: null);

        var result = await port.OpenAsync(accountId, BrowserOpenTarget.ChatGpt);

        Assert.False(result.IsSuccess);
        Assert.Equal("CHROME_NOT_FOUND", result.Status);
        await using var context = await _database.ContextFactory.CreateDbContextAsync();
        Assert.Empty(await context.Set<BrowserProfileBindingRecord>().ToListAsync());
    }

    [Fact]
    public async Task LaunchFailureIsPersistedWithoutClaimingBrowserOpened()
    {
        var accountId = await CreateAccountAsync("failed-browser@example.test");
        var launcher = new CapturingChromeLauncher { ShouldSucceed = false };
        using var port = CreatePort(launcher);

        var result = await port.OpenAsync(accountId, BrowserOpenTarget.ChatGpt);

        Assert.False(result.IsSuccess);
        Assert.Equal("BrowserLaunchFailed", result.Status);
        Assert.Equal(BrowserProfileState.LaunchFailed.ToString(), result.Binding?.State);
        Assert.Single(launcher.ProfileDirectories);
    }

    [Fact]
    public async Task TraversalProfileBindingIsRejectedBeforeLaunch()
    {
        var accountId = await CreateAccountAsync("traversal@example.test");
        await using (var context = await _database.ContextFactory.CreateDbContextAsync())
        {
            context.Add(new BrowserProfileBindingRecord(
                Guid.NewGuid(),
                accountId,
                "openai-chatgpt",
                "Chrome",
                "synthetic-profile",
                Path.Combine(_database.Root, ".local", "outside-profile"),
                DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        var launcher = new CapturingChromeLauncher { ShouldSucceed = true };
        using var port = CreatePort(launcher);

        var result = await port.OpenAsync(accountId, BrowserOpenTarget.ChatGpt);

        Assert.False(result.IsSuccess);
        Assert.Equal("BROWSER_PROFILE_PATH_INVALID", result.Status);
        Assert.Empty(launcher.ProfileDirectories);
    }

    private BrowserProfileUiPort CreatePort(CapturingChromeLauncher launcher, string? chromePath = "C:\\Synthetic\\chrome.exe")
        => new(
            _database.ContextFactory,
            new LocalDataPaths(_database.Root),
            new FixedChromeResolver(chromePath),
            launcher);

    private async Task<Guid> CreateAccountAsync(string email)
    {
        await using var scope = _database.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var result = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = email,
            DisplayName = email.Split('@')[0],
            Provider = "openai",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified"
        });
        Assert.True(result.IsSuccess, result.Message);

        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        return (await reader.GetAccountsAsync(email)).Single().AccountId;
    }

    private sealed class FixedChromeResolver(string? path) : IChromeExecutableResolver
    {
        public string? Resolve() => path;
    }

    private sealed class CapturingChromeLauncher : IChromeProcessLauncher
    {
        public bool ShouldSucceed { get; init; }
        public List<string> ProfileDirectories { get; } = [];
        public List<string> OfficialUrls { get; } = [];

        public bool TryLaunch(string executablePath, string profileDirectory, string officialUrl, out string? safeError)
        {
            ProfileDirectories.Add(profileDirectory);
            OfficialUrls.Add(officialUrl);
            safeError = ShouldSucceed ? null : "Synthetic launch failure.";
            return ShouldSucceed;
        }
    }
}
