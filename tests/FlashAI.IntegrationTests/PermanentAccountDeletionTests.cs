using System.Text.Json;
using System.Globalization;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Domain;
using FlashAI.Modules.Accounts.Features.BrowseAccounts.Application;
using FlashAI.Modules.Activity.Domain;
using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Authenticator.Contracts;
using FlashAI.Modules.Authenticator.Integration;
using FlashAI.Modules.BackupRestore.Application;
using FlashAI.Modules.BackupRestore.Integration;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FLASH_ROUTER.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class PermanentAccountDeletionTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SqliteAccountDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task WrongEmailLeavesAccountAndBrowserUntouched()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        Assert.True((await commands.CreateAccountAsync(NewModel("keep@example.test", "Keep"))).IsSuccess);
        var accountId = (await scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>()
            .GetAccountsAsync("keep@example.test")).Single().AccountId;

        var protectionProvider = CreateProtectionProvider();
        var paths = new LocalDataPaths(_fixture.Root);
        var vault = new FileSecretVault(Path.Combine(paths.Security, "vault"), protectionProvider);
        var browser = new TestBrowserSessionManager();
        var service = CreateService(vault, paths, browser, new ChromeProfileLockInspector(), protectionProvider);

        var result = await service.DeletePermanentlyAsync(accountId, "wrong@example.test");

        Assert.False(result.IsSuccess);
        Assert.Equal("CONFIRMATION_MISMATCH", result.ErrorCode);
        Assert.Equal(0, browser.CloseCount);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>()
            .GetAccountDetailAsync(accountId));
    }

    [Fact]
    public async Task PermanentDeleteRemovesCurrentDataSecretsBrowserProfileAndManagedBackups()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        Assert.True((await commands.CreateAccountAsync(NewModel("remove@example.test", "Remove", ["private-tag"]))).IsSuccess);
        Assert.True((await commands.CreateAccountAsync(NewModel("keep@example.test", "Keep"))).IsSuccess);
        var accountId = (await reader.GetAccountsAsync("remove@example.test")).Single().AccountId;
        var keepAccountId = (await reader.GetAccountsAsync("keep@example.test")).Single().AccountId;

        var protectionProvider = CreateProtectionProvider();
        var paths = new LocalDataPaths(_fixture.Root);
        var vault = new FileSecretVault(Path.Combine(paths.Security, "vault"), protectionProvider);
        var authenticator = new DpapiAuthenticatorUiPort(vault, new Rfc6238TotpGenerator());
        Assert.True((await authenticator.UnlockVaultAsync("synthetic-passphrase-2026")).IsSuccess);
        Assert.True((await authenticator.AddCredentialAsync(
            accountId,
            "Synthetic provider",
            "Remove account",
            "JBSWY3DPEHPK3PXP")).IsSuccess);

        var connectionId = Guid.NewGuid();
        var vaultKey = SqliteAccountCredentialUiPort.GetVaultKey(connectionId);
        await vault.StoreAsync(vaultKey, "synthetic-secret"u8.ToArray());
        await using (var context = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var now = DateTimeOffset.UtcNow;
            context.Add(new ProviderConnectionRecord(
                connectionId,
                accountId,
                "openai-chatgpt",
                ConnectionKind.Manual,
                "Synthetic ChatGPT connection",
                null,
                ConnectionStatus.CredentialStored,
                now,
                now));
            context.Add(new ProviderCredentialReferenceRecord(
                Guid.NewGuid(),
                connectionId,
                "openai-chatgpt",
                "synthetic-credential",
                ProviderCredentialKind.ManualWebCredential,
                vaultKey,
                CredentialProtectionMetadata.CurrentScope,
                now,
                now));
            context.Add(new ActivityEventRecord(
                Guid.NewGuid(),
                accountId,
                "SyntheticEvent",
                "Info",
                "Synthetic account event",
                null,
                Guid.NewGuid().ToString("N"),
                now));

            var profilePath = Path.Combine(_fixture.Root, ".local", "browser-profiles", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profilePath);
            await File.WriteAllTextAsync(Path.Combine(profilePath, "synthetic-profile-data"), "synthetic");
            context.Add(new BrowserProfileBindingRecord(
                Guid.NewGuid(),
                accountId,
                "openai-chatgpt",
                "Chrome",
                Path.GetFileName(profilePath),
                profilePath,
                now));
            await context.SaveChangesAsync();
        }

        var backupPort = new SqliteBackupRestoreUiPort(
            paths,
            scope.ServiceProvider.GetRequiredService<SqliteConnectionFactory>(),
            protectionProvider);
        Assert.True((await backupPort.RequestCreateBackupAsync("Before account deletion", "Toàn phần")).IsSuccess);
        var backupId = Assert.Single(await backupPort.GetBackupsAsync()).BackupId;
        var backupProtector = protectionProvider.CreateProtector("FLASH ROUTER", "local-backup", PortableBackupFormat.CurrentVersion);
        var backupPayload = await File.ReadAllBytesAsync(Path.Combine(paths.Backups, backupId.ToString("N"), PortableBackupFormat.DatabasePayloadFileName));
        var backupDatabaseBytes = backupProtector.Unprotect(backupPayload);
        var inspectDatabase = Path.Combine(_fixture.Root, "inspect-backup.sqlite");
        await File.WriteAllBytesAsync(inspectDatabase, backupDatabaseBytes);
        await using (var inspectConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = inspectDatabase, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        {
            await inspectConnection.OpenAsync();
            await using var inspectCommand = inspectConnection.CreateCommand();
            inspectCommand.CommandText = "SELECT Id, lower(replace(Id, '-', '')) FROM AccountProfiles;";
            await using var idReader = await inspectCommand.ExecuteReaderAsync();
            var ids = new List<string>();
            var normalizedIds = new List<string>();
            while (await idReader.ReadAsync())
            {
                ids.Add(idReader.GetString(0));
                normalizedIds.Add(idReader.GetString(1));
            }
            Assert.Contains(accountId, ids.Select(Guid.Parse));
            Assert.Contains(accountId.ToString("N"), normalizedIds);
        }
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(backupPayload);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(backupDatabaseBytes);

        var browser = new TestBrowserSessionManager();
        var service = CreateService(vault, paths, browser, new ChromeProfileLockInspector(), protectionProvider, authenticator);
        var result = await service.DeletePermanentlyAsync(accountId, " REMOVE@example.test ");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Contains("dọn 1 bản sao lưu", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await reader.GetAccountDetailAsync(accountId));
        Assert.NotNull(await reader.GetAccountDetailAsync(keepAccountId));
        Assert.Null(await vault.ReadAsync(vaultKey));
        Assert.Empty(await authenticator.GetCredentialsAsync(accountId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM AccountProfiles;"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM ActivityEvents WHERE AccountId = $id;", accountId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM BrowserProfileBindings WHERE AccountId = $id;", accountId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM ProviderConnections WHERE AccountId = $id;", accountId));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_fixture.Root, ".local", "browser-profiles")));

        var refreshedBackup = Assert.Single(await backupPort.GetBackupsAsync());
        Assert.Equal(backupId, refreshedBackup.BackupId);
        Assert.Equal("ok", refreshedBackup.Status);
        var manifestPath = Path.Combine(paths.Backups, backupId.ToString("N"), PortableBackupFormat.ManifestFileName);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllBytesAsync(manifestPath), JsonOptions);
        Assert.NotNull(manifest);
        Assert.Equal(1, manifest.AccountCount);
        Assert.Equal(0, manifest.CredentialReferenceCount);
        Assert.True((await backupPort.RequestRestoreAsync(backupId, confirmed: true)).IsSuccess);

        authenticator.Dispose();

        async Task<int> CountAsync(string sql, Guid? id = null)
        {
            await using var context = await _fixture.ContextFactory.CreateDbContextAsync();
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            if (id.HasValue)
            {
                command.Parameters.Add(new SqliteParameter("$id", id.Value.ToString()));
            }
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
    }

    [Fact]
    public async Task ActiveChromeProfileBlocksDeletionWithoutRemovingStoredSecrets()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<SqliteAccountCommandPort>();
        var reader = scope.ServiceProvider.GetRequiredService<SqliteAccountWorkspaceReader>();
        Assert.True((await commands.CreateAccountAsync(NewModel("locked@example.test", "Locked"))).IsSuccess);
        var accountId = (await reader.GetAccountsAsync("locked@example.test")).Single().AccountId;

        var protectionProvider = CreateProtectionProvider();
        var paths = new LocalDataPaths(_fixture.Root);
        var vault = new FileSecretVault(Path.Combine(paths.Security, "vault"), protectionProvider);
        var connectionId = Guid.NewGuid();
        var vaultKey = SqliteAccountCredentialUiPort.GetVaultKey(connectionId);
        await vault.StoreAsync(vaultKey, "synthetic-secret"u8.ToArray());
        var profilePath = Path.Combine(_fixture.Root, ".local", "browser-profiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profilePath);
        await using (var context = await _fixture.ContextFactory.CreateDbContextAsync())
        {
            var now = DateTimeOffset.UtcNow;
            context.Add(new ProviderConnectionRecord(connectionId, accountId, "openai-chatgpt", ConnectionKind.Manual, "Locked", null, ConnectionStatus.CredentialStored, now, now));
            context.Add(new ProviderCredentialReferenceRecord(Guid.NewGuid(), connectionId, "openai-chatgpt", "locked-secret", ProviderCredentialKind.ManualWebCredential, vaultKey, null, now, now));
            context.Add(new BrowserProfileBindingRecord(Guid.NewGuid(), accountId, "openai-chatgpt", "Chrome", Path.GetFileName(profilePath), profilePath, now));
            await context.SaveChangesAsync();
        }

        var lockInspector = new TestProfileLockInspector(BrowserProfileLockState.Active);
        var service = CreateService(vault, paths, new TestBrowserSessionManager(), lockInspector, protectionProvider);
        var result = await service.DeletePermanentlyAsync(accountId, "locked@example.test");

        Assert.False(result.IsSuccess);
        Assert.Equal("PROFILE_IN_USE", result.ErrorCode);
        Assert.NotNull(await reader.GetAccountDetailAsync(accountId));
        Assert.NotNull(await vault.ReadAsync(vaultKey));
        Assert.True(Directory.Exists(profilePath));
    }

    private SqlitePermanentAccountDeletionService CreateService(
        ISecretVault vault,
        LocalDataPaths paths,
        TestBrowserSessionManager browser,
        IChromeProfileLockInspector lockInspector,
        IDataProtectionProvider protectionProvider,
        IAuthenticatorUiPort? authenticator = null)
        => new(
            _fixture.ContextFactory,
            vault,
            paths,
            browser,
            new PreviewBrowserAuthOrchestrator(),
            lockInspector,
            new BrowserProfileOwnershipCoordinator(lockInspector),
            authenticator ?? new DpapiAuthenticatorUiPort(vault, new Rfc6238TotpGenerator()),
            protectionProvider);

    private IDataProtectionProvider CreateProtectionProvider()
        => DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_fixture.Root, "test-key-ring")));

    private static AccountEditorVm NewModel(string email, string displayName, IReadOnlyList<string>? tags = null)
        => new()
        {
            Email = email,
            DisplayName = displayName,
            Provider = "openai-chatgpt",
            RecordedPlan = "Chưa rõ",
            Status = "Unverified",
            Tags = tags?.ToList() ?? []
        };

    private sealed class TestProfileLockInspector(BrowserProfileLockState state) : IChromeProfileLockInspector
    {
        public BrowserProfileLockState Inspect(string profileDirectory) => state;
    }

    private sealed class TestBrowserSessionManager : IAccountBrowserSessionManager
    {
        public int CloseCount { get; private set; }

        public Task<BrowserRuntimeSnapshot> GetStatusAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot(accountId));

        public Task<BrowserOperationResult> OpenAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(Operation(accountId));

        public Task<BrowserOperationResult> CloseAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            CloseCount++;
            return Task.FromResult(Operation(accountId));
        }

        public Task<BrowserOperationResult> ReopenAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(Operation(accountId));

        public Task<SessionObservationResult> CheckSessionAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(new SessionObservationResult(true, "OK", "", Snapshot(accountId)));

        public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static BrowserOperationResult Operation(Guid accountId)
            => new(true, "CLOSED", "", Snapshot(accountId));

        private static BrowserRuntimeSnapshot Snapshot(Guid accountId)
            => new(accountId, null, "openai-chatgpt", AccountBrowserRuntimeState.Closed,
                AuthenticationObservationStatus.NotChecked, IdentityVerificationStatus.NotChecked, DateTimeOffset.UtcNow);
    }
}
