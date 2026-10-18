using System.Security.Cryptography;
using System.Text;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Persistence;
using FlashAI.Modules.Activity.Persistence;
using FlashAI.Modules.BackupRestore.Application;
using FlashAI.Modules.BackupRestore.Contracts;
using FlashAI.Modules.Connections.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashAI.IntegrationTests;

public sealed class Be06BackupRecoveryTests
{
    [Fact]
    public async Task EncryptedBackupAndDisposableRestorePreserveActiveDatabase()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        var commands = fixture.Services.GetRequiredService<SqliteAccountCommandPort>();
        var created = await commands.CreateAccountAsync(new AccountEditorVm
        {
            Email = "synthetic-backup@example.test",
            DisplayName = "Synthetic backup account",
            Provider = "synthetic",
            RecordedPlan = "Unverified",
            Status = "Unverified"
        });
        Assert.True(created.IsSuccess, created.Message);

        await fixture.Vault.StoreAsync("synthetic/backup", Encoding.UTF8.GetBytes("synthetic-vault-value"));
        var before = await BackupFixture.HashAsync(fixture.Paths.DatabaseFile);

        var result = await fixture.Port.RequestCreateBackupAsync("synthetic-backup", "Toàn phần");
        Assert.True(result.IsSuccess, result.Message);

        var backup = Assert.Single(await fixture.Port.GetBackupsAsync());
        Assert.True(backup.IsEncrypted);
        Assert.Equal("Toàn phần", backup.Type);
        Assert.Equal("ok", backup.Status, ignoreCase: true);

        var confirmation = await fixture.Port.RequestRestoreAsync(backup.BackupId);
        Assert.Equal("RESTORE_CONFIRMATION_REQUIRED", confirmation.ErrorCode);

        var restored = await fixture.Port.RequestRestoreAsync(backup.BackupId, confirmed: true);
        Assert.True(restored.IsSuccess, restored.Message);
        Assert.Equal(before, await BackupFixture.HashAsync(fixture.Paths.DatabaseFile));

        var backupDirectory = Directory.EnumerateDirectories(fixture.Paths.Backups).Single();
        Assert.Single(Directory.EnumerateFiles(Path.Combine(backupDirectory, "vault"), "*.secret"));
        Assert.DoesNotContain("synthetic-vault-value", File.ReadAllText(Path.Combine(backupDirectory, "manifest.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorruptedPayloadIsRejectedWithoutTouchingSourceDatabase()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        var result = await fixture.Port.RequestCreateBackupAsync("synthetic-corrupt", "Toàn phần");
        Assert.True(result.IsSuccess, result.Message);
        var backup = Assert.Single(await fixture.Port.GetBackupsAsync());
        var backupDirectory = Directory.EnumerateDirectories(fixture.Paths.Backups).Single();
        var payload = Path.Combine(backupDirectory, "database.flashdb");
        var bytes = await File.ReadAllBytesAsync(payload);
        bytes[^1] ^= 0x55;
        await File.WriteAllBytesAsync(payload, bytes);

        var restored = await fixture.Port.RequestRestoreAsync(backup.BackupId, confirmed: true);
        Assert.Equal("BACKUP_CHECKSUM_INVALID", restored.ErrorCode);
        Assert.Equal("ok", await BackupFixture.ReadScalarAsync(fixture.Paths.DatabaseFile, "PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task BackupNamesCannotEscapeTheBackupDirectory()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        var result = await fixture.Port.RequestCreateBackupAsync("..\\outside", "Toàn phần");

        Assert.False(result.IsSuccess);
        Assert.Equal("BACKUP_NAME_INVALID", result.ErrorCode);
        Assert.Empty(Directory.Exists(fixture.Paths.Backups)
            ? Directory.EnumerateDirectories(fixture.Paths.Backups)
            : []);
    }

    private sealed class BackupFixture : IAsyncDisposable
    {
        private BackupFixture(
            string root,
            LocalDataPaths paths,
            ServiceProvider services,
            SqliteBackupRestoreUiPort port,
            FileSecretVault vault,
            IDataProtectionProvider dataProtection)
        {
            Root = root;
            Paths = paths;
            Services = services;
            Port = port;
            Vault = vault;
            DataProtection = dataProtection;
        }

        public string Root { get; }
        public LocalDataPaths Paths { get; }
        public ServiceProvider Services { get; }
        public SqliteBackupRestoreUiPort Port { get; }
        public FileSecretVault Vault { get; }
        public IDataProtectionProvider DataProtection { get; }

        public static async Task<BackupFixture> CreateAsync()
        {
            var root = Path.Combine(WorkspaceRoot(), ".local", "test-data", "be06-backup-" + Guid.NewGuid().ToString("N"));
            var paths = new LocalDataPaths(root);
            var dataProtection = DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(root, "keys")),
                builder => builder.SetApplicationName("FLASH ROUTER BE-06 TEST"));
            var vault = new FileSecretVault(Path.Combine(paths.Security, "vault"), dataProtection);
            var services = new ServiceCollection();
            services.AddSingleton(paths);
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<SqliteConnectionFactory>();
            services.AddSingleton<IFlashAiModelContributor, AccountModelContributor>();
            services.AddSingleton<IFlashAiModelContributor, ConnectionModelContributor>();
            services.AddSingleton<IFlashAiModelContributor, ActivityModelContributor>();
            services.AddDbContextFactory<FlashAiDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(
                    serviceProvider.GetRequiredService<SqliteConnectionFactory>().CreateConnectionString(),
                    sqlite => sqlite.MigrationsAssembly("FLASH_ROUTER"));
            });
            services.AddSingleton<MigrationCoordinator>();
            services.AddScoped<SqliteAccountCommandPort>();
            var serviceProvider = services.BuildServiceProvider();
            var connections = serviceProvider.GetRequiredService<SqliteConnectionFactory>();
            await connections.PrepareDatabaseAsync();
            await serviceProvider.GetRequiredService<MigrationCoordinator>().MigrateAsync();
            return new BackupFixture(root, paths, serviceProvider, new SqliteBackupRestoreUiPort(paths, connections, dataProtection), vault, dataProtection);
        }

        public async ValueTask DisposeAsync()
        {
            Vault.Dispose();
            if (DataProtection is IDisposable disposableDataProtection)
            {
                disposableDataProtection.Dispose();
            }

            if (Services is IAsyncDisposable asyncServices)
            {
                await asyncServices.DisposeAsync();
            }

            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        public static async Task<string> HashAsync(string path)
        {
            SqliteConnection.ClearAllPools();
            await using var stream = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream));
        }

        public static async Task<string> ReadScalarAsync(string path, string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string WorkspaceRoot()
        {
            var current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the FLASH ROUTER solution root.");
        }
    }
}
