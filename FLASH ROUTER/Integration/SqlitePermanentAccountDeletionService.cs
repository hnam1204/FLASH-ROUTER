using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Domain;
using FlashAI.Modules.Activity.Domain;
using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Authenticator.Contracts;
using FlashAI.Modules.Authenticator.Integration;
using FlashAI.Modules.BackupRestore.Integration;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FLASH_ROUTER.Integration;

/// <summary>
/// Permanently removes one account and all app-managed data associated with it.
/// Prior local full backups are scrubbed in place before the active database is committed.
/// </summary>
public sealed class SqlitePermanentAccountDeletionService : IPermanentAccountDeletionService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> AccountGates = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly ISecretVault _vault;
    private readonly LocalDataPaths _paths;
    private readonly IAccountBrowserSessionManager _browserSessions;
    private readonly IBrowserAuthOrchestrator _authOrchestrator;
    private readonly IChromeProfileLockInspector _profileLockInspector;
    private readonly IBrowserProfileOwnershipCoordinator _profileOwnership;
    private readonly IAuthenticatorUiPort _authenticator;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IDataProtector _backupProtector;

    public SqlitePermanentAccountDeletionService(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        ISecretVault vault,
        LocalDataPaths paths,
        IAccountBrowserSessionManager browserSessions,
        IBrowserAuthOrchestrator authOrchestrator,
        IChromeProfileLockInspector profileLockInspector,
        IBrowserProfileOwnershipCoordinator profileOwnership,
        IAuthenticatorUiPort authenticator,
        IDataProtectionProvider dataProtectionProvider)
    {
        _contextFactory = contextFactory;
        _vault = vault;
        _paths = paths;
        _browserSessions = browserSessions;
        _authOrchestrator = authOrchestrator;
        _profileLockInspector = profileLockInspector;
        _profileOwnership = profileOwnership;
        _authenticator = authenticator;
        _dataProtectionProvider = dataProtectionProvider;
        _backupProtector = dataProtectionProvider.CreateProtector(
            "FLASH ROUTER",
            "local-backup",
            PortableBackupFormat.CurrentVersion);
    }

    public async Task<OperationResultVm> DeletePermanentlyAsync(
        Guid accountId,
        string confirmedEmail,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return OperationResultVm.Failure("ACCOUNT_REQUIRED", "Không xác định được tài khoản cần xóa.");
        }

        if (string.IsNullOrWhiteSpace(confirmedEmail))
        {
            return OperationResultVm.Failure("CONFIRMATION_REQUIRED", "Hãy nhập đúng email của tài khoản để xác nhận.");
        }

        var accountGate = AccountGates.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        try
        {
            await accountGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResultVm.Failure("CANCELLED", "Yêu cầu xóa đã bị hủy trước khi bắt đầu.");
        }

        var backupGateAcquired = false;
        var profileLeases = new List<IDisposable>();
        try
        {
            string email;
            await using (var readContext = await _contextFactory.CreateDbContextAsync(cancellationToken))
            {
                email = await readContext.Set<AccountRecord>()
                    .AsNoTracking()
                    .Where(account => account.Id == accountId)
                    .Select(account => account.Email)
                    .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
            }

            if (string.IsNullOrEmpty(email))
            {
                return OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản.");
            }

            if (!EmailMatches(email, confirmedEmail))
            {
                return OperationResultVm.Failure("CONFIRMATION_MISMATCH", "Email xác nhận không khớp với tài khoản.");
            }

            if (!await _vault.IsAvailableAsync(cancellationToken))
            {
                return OperationResultVm.Failure("VAULT_UNAVAILABLE", "Kho bảo mật chưa sẵn sàng; chưa xóa dữ liệu tài khoản.");
            }

            // Once the user confirms, finish or report a retryable failure; a component disconnect
            // must not interrupt cleanup between deleting secrets and committing the account row.
            var cancellationResults = await _authOrchestrator.CancelAccountAttemptsAsync(accountId, CancellationToken.None);
            if (cancellationResults.Any(result => !result.IsSuccess))
            {
                return OperationResultVm.Failure("LOGIN_ATTEMPT_CLEANUP_FAILED", "Không thể đóng toàn bộ luồng đăng nhập đang chạy; hãy thử lại.");
            }

            var closeResult = await _browserSessions.CloseAsync(accountId, CancellationToken.None);
            if (!closeResult.IsSuccess)
            {
                return OperationResultVm.Failure("BROWSER_CLOSE_FAILED", "Không thể đóng phiên Chrome được quản lý; hãy đóng Chrome rồi thử lại.");
            }

            await BackupMaintenanceGate.Semaphore.WaitAsync(CancellationToken.None);
            backupGateAcquired = true;

            await using var context = await _contextFactory.CreateDbContextAsync(CancellationToken.None);
            await context.Database.OpenConnectionAsync(CancellationToken.None);
            await context.Database.ExecuteSqlRawAsync("PRAGMA secure_delete = ON;", CancellationToken.None);
            await using var transaction = await context.Database.BeginTransactionAsync(CancellationToken.None);

            // Take SQLite's writer lock before taking the cleanup snapshot. This closes the race
            // where another request could add a credential after the vault keys were collected.
            var lockedRows = await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AccountProfiles SET Version = Version + 1 WHERE Id = {accountId};",
                CancellationToken.None);
            if (lockedRows != 1)
            {
                return OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản.");
            }

            var currentEmail = await context.Set<AccountRecord>()
                .AsNoTracking()
                .Where(account => account.Id == accountId)
                .Select(account => account.Email)
                .SingleOrDefaultAsync(CancellationToken.None);
            if (currentEmail is null)
            {
                return OperationResultVm.Failure("NOT_FOUND", "Không tìm thấy tài khoản.");
            }

            if (!EmailMatches(currentEmail, confirmedEmail))
            {
                return OperationResultVm.Failure("CONFIRMATION_MISMATCH", "Email tài khoản đã thay đổi; hãy tải lại và xác nhận lại.");
            }

            var connectionIds = await context.Set<ProviderConnectionRecord>()
                .AsNoTracking()
                .Where(connection => connection.AccountId == accountId)
                .Select(connection => connection.Id)
                .ToArrayAsync(CancellationToken.None);
            var vaultKeys = await CollectLiveVaultKeysAsync(context, connectionIds);
            var profileDirectories = await context.Set<BrowserProfileBindingRecord>()
                .AsNoTracking()
                .Where(binding => binding.AccountId == accountId)
                .Select(binding => binding.ProfileDirectoryReference)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct()
                .ToArrayAsync(CancellationToken.None);

            foreach (var profileDirectory in profileDirectories)
            {
                if (!TryValidateManagedProfile(profileDirectory, out var canonicalPath, out var error))
                {
                    return OperationResultVm.Failure("PROFILE_PATH_REJECTED", error);
                }

                var ownership = _profileOwnership.TryAcquire(canonicalPath, BrowserProfileOwnerKind.PermanentDeletion);
                if (!ownership.IsAcquired || ownership.Lease is null)
                {
                    return OperationResultVm.Failure("PROFILE_IN_USE", ownership.Message);
                }

                profileLeases.Add(ownership.Lease);
                if (_profileLockInspector.Inspect(canonicalPath) == BrowserProfileLockState.Active)
                {
                    return OperationResultVm.Failure("PROFILE_IN_USE", "Profile Chrome đang được sử dụng; hãy đóng cửa sổ Chrome rồi thử lại.");
                }

                if (Directory.Exists(canonicalPath) && ContainsReparsePoint(canonicalPath))
                {
                    return OperationResultVm.Failure("PROFILE_PATH_REJECTED", "Profile chứa liên kết hệ thống tệp; chưa xóa dữ liệu để tránh chạm ra ngoài vùng quản lý.");
                }
            }

            var scrubbedBackupCount = await ScrubManagedBackupsAsync(accountId);

            foreach (var key in vaultKeys)
            {
                await _vault.RemoveAsync(key, CancellationToken.None);
            }

            var authenticatorResult = await _authenticator.RemoveAccountCredentialsAsync(accountId, CancellationToken.None);
            if (!authenticatorResult.IsSuccess)
            {
                return OperationResultVm.Failure(
                    authenticatorResult.ErrorCode ?? "AUTHENTICATOR_CLEANUP_FAILED",
                    "Không thể xóa toàn bộ khóa xác minh hai bước; dữ liệu tài khoản vẫn được giữ để có thể thử lại.");
            }

            foreach (var profileDirectory in profileDirectories)
            {
                var canonicalPath = Path.GetFullPath(profileDirectory);
                if (_profileLockInspector.Inspect(canonicalPath) == BrowserProfileLockState.Active)
                {
                    return OperationResultVm.Failure("PROFILE_IN_USE", "Chrome đã mở lại profile trong lúc dọn dữ liệu; hãy đóng cửa sổ rồi thử lại.");
                }

                if (Directory.Exists(canonicalPath))
                {
                    Directory.Delete(canonicalPath, recursive: true);
                }
            }

            var connectionQuery = context.Set<ProviderConnectionRecord>()
                .Where(connection => connection.AccountId == accountId)
                .Select(connection => connection.Id);

            await context.Set<ProviderObservationRecord>()
                .Where(item => connectionQuery.Contains(item.ConnectionId))
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<ProviderCheckRunRecord>()
                .Where(item => connectionQuery.Contains(item.ConnectionId))
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<ConnectionCheckRecord>()
                .Where(item => connectionQuery.Contains(item.ConnectionId))
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<ProviderSyncStateRecord>()
                .Where(item => connectionQuery.Contains(item.ConnectionId))
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<ProviderCredentialReferenceRecord>()
                .Where(item => connectionQuery.Contains(item.ConnectionId))
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<TokenVaultReferenceRecord>()
                .Where(item => connectionQuery.Contains(item.ConnectionId))
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<ConnectionSessionRecord>()
                .Where(item => item.AccountId == accountId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<OAuthRegistrationRecord>()
                .Where(item => item.AccountId == accountId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<BrowserProfileBindingRecord>()
                .Where(item => item.AccountId == accountId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<ActivityEventRecord>()
                .Where(item => item.AccountId == accountId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<ProviderAccountObservationRecord>()
                .Where(item => item.AccountId == accountId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<AccountTagRecord>()
                .Where(item => item.AccountId == accountId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await context.Set<ProviderConnectionRecord>()
                .Where(item => item.AccountId == accountId)
                .ExecuteDeleteAsync(CancellationToken.None);

            var deletedAccounts = await context.Set<AccountRecord>()
                .Where(account => account.Id == accountId)
                .ExecuteDeleteAsync(CancellationToken.None);
            if (deletedAccounts != 1)
            {
                return OperationResultVm.Failure("DELETE_COMMIT_FAILED", "Không thể xác nhận đã xóa tài khoản; hãy tải lại dữ liệu.");
            }

            await transaction.CommitAsync(CancellationToken.None);
            await CompactDatabaseBestEffortAsync(context);
            return OperationResultVm.Success($"Đã xóa vĩnh viễn tài khoản cùng dữ liệu liên quan; đã dọn {scrubbedBackupCount} bản sao lưu cục bộ.");
        }
        catch (SecretVaultException)
        {
            return OperationResultVm.Failure("VAULT_CLEANUP_FAILED", "Không thể dọn hết bí mật đã mã hóa; tài khoản được giữ lại để thử lại.");
        }
        catch (BackupScrubException exception)
        {
            return OperationResultVm.Failure(exception.ErrorCode, exception.Message);
        }
        catch (OperationCanceledException)
        {
            return OperationResultVm.Failure("CANCELLED", "Yêu cầu xóa đã bị hủy trước khi bắt đầu dọn dữ liệu.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SqliteException
            or DbUpdateException
            or InvalidDataException
            or JsonException
            or CryptographicException)
        {
            return OperationResultVm.Failure("PERMANENT_DELETE_FAILED", "Chưa thể hoàn tất xóa. Tài khoản có thể đã được dọn một phần; hãy thử lại để hoàn tất.");
        }
        catch (Exception)
        {
            return OperationResultVm.Failure("PERMANENT_DELETE_FAILED", "Chưa thể hoàn tất xóa. Tài khoản có thể đã được dọn một phần; hãy thử lại để hoàn tất.");
        }
        finally
        {
            foreach (var lease in profileLeases)
            {
                lease.Dispose();
            }

            if (backupGateAcquired)
            {
                BackupMaintenanceGate.Semaphore.Release();
            }

            accountGate.Release();
        }
    }

    private static bool EmailMatches(string stored, string confirmed)
        => string.Equals(stored.Trim(), confirmed.Trim(), StringComparison.OrdinalIgnoreCase);

    private static async Task<HashSet<string>> CollectLiveVaultKeysAsync(
        FlashAiDbContext context,
        IReadOnlyCollection<Guid> connectionIds)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connectionId in connectionIds)
        {
            keys.Add(SqliteAccountCredentialUiPort.GetVaultKey(connectionId));
            keys.Add(OAuthTokenVaultStore.GetAccessKey(connectionId));
            keys.Add(OAuthTokenVaultStore.GetRefreshKey(connectionId));
            keys.Add(OAuthTokenVaultStore.GetIdTokenKey(connectionId));
        }

        foreach (var key in await context.Set<ProviderCredentialReferenceRecord>()
                     .AsNoTracking()
                     .Where(reference => connectionIds.Contains(reference.ConnectionId))
                     .Select(reference => reference.VaultKey)
                     .ToArrayAsync(CancellationToken.None))
        {
            keys.Add(key);
        }

        foreach (var reference in await context.Set<TokenVaultReferenceRecord>()
                     .AsNoTracking()
                     .Where(reference => connectionIds.Contains(reference.ConnectionId))
                     .ToArrayAsync(CancellationToken.None))
        {
            keys.Add(reference.AccessTokenKey);
            keys.Add(reference.RefreshTokenKey);
            keys.Add(reference.IdTokenKey);
        }

        keys.RemoveWhere(string.IsNullOrWhiteSpace);
        return keys;
    }

    private bool TryValidateManagedProfile(string directory, out string canonicalPath, out string error)
    {
        canonicalPath = string.Empty;
        error = "Profile Chrome không thuộc vùng dữ liệu do ứng dụng quản lý; chưa xóa để bảo vệ dữ liệu khác.";
        try
        {
            canonicalPath = Path.GetFullPath(directory);
            var candidatePath = canonicalPath;
            var roots = new[]
            {
                Path.GetFullPath(Path.Combine(_paths.Root, ".local", "browser-profiles")),
                Path.GetFullPath(_paths.AutomationBrowserProfiles)
            };

            var root = roots.FirstOrDefault(candidate => IsStrictChild(candidate, candidatePath));
            if (root is null || HasReparsePointInPath(candidatePath))
            {
                return false;
            }

            if (File.Exists(candidatePath) || (Directory.Exists(candidatePath) && !Directory.Exists(root)))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsStrictChild(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative != "."
            && !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool HasReparsePointInPath(string target)
    {
        var current = new DirectoryInfo(target);
        while (current is not null)
        {
            if ((current.Exists || File.Exists(current.FullName))
                && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool ContainsReparsePoint(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(entry);
                }
            }
        }

        return false;
    }

    private async Task<int> ScrubManagedBackupsAsync(Guid accountId)
    {
        if (!Directory.Exists(_paths.Backups))
        {
            return 0;
        }

        EnsureNoReparsePath(_paths.Backups);
        var scrubbedCount = 0;
        foreach (var backupDirectory in Directory.EnumerateDirectories(_paths.Backups).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(Path.GetFileName(backupDirectory), out var backupId))
            {
                continue;
            }

            if (await ScrubBackupAsync(backupDirectory, backupId, accountId))
            {
                scrubbedCount++;
            }
        }

        return scrubbedCount;
    }

    private async Task<bool> ScrubBackupAsync(string backupDirectory, Guid backupId, Guid accountId)
    {
        EnsureNoReparsePath(backupDirectory);
        var manifestPath = Path.Combine(backupDirectory, PortableBackupFormat.ManifestFileName);
        var payloadPath = Path.Combine(backupDirectory, PortableBackupFormat.DatabasePayloadFileName);
        EnsureRegularFile(manifestPath);
        EnsureRegularFile(payloadPath);

        BackupManifest manifest;
        await using (var manifestStream = File.OpenRead(manifestPath))
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, JsonOptions)
                ?? throw new BackupScrubException("BACKUP_MANIFEST_INVALID", "Có bản sao lưu cần xử lý nhưng manifest không hợp lệ; chưa xóa tài khoản.");
        }

        if (manifest.BackupId != backupId
            || !string.Equals(manifest.FormatVersion, PortableBackupFormat.CurrentVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.EncryptionStatus, "DataProtection-DPAPI", StringComparison.Ordinal))
        {
            throw new BackupScrubException("BACKUP_INCOMPATIBLE", "Có bản sao lưu cục bộ không tương thích; chưa xóa tài khoản để tránh giữ lại bản khôi phục cũ.");
        }

        var encryptedPayload = await File.ReadAllBytesAsync(payloadPath);
        byte[] snapshot;
        try
        {
            var expectedHash = Convert.FromHexString(manifest.DatabasePayloadSha256);
            var actualHash = SHA256.HashData(encryptedPayload);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                {
                    throw new BackupScrubException("BACKUP_CHECKSUM_INVALID", "Checksum của một bản sao lưu không khớp; chưa xóa tài khoản.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expectedHash);
                CryptographicOperations.ZeroMemory(actualHash);
            }

            snapshot = _backupProtector.Unprotect(encryptedPayload);
        }
        catch (BackupScrubException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new BackupScrubException("BACKUP_ENCRYPTION_INVALID", "Không thể kiểm tra một bản sao lưu cục bộ bằng khóa hiện tại; chưa xóa tài khoản.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptedPayload);
        }

        var tempRoot = Path.GetFullPath(_paths.Temp);
        EnsureNoReparsePath(tempRoot);
        Directory.CreateDirectory(tempRoot);
        var tempDatabase = Path.Combine(tempRoot, "account-delete-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            await File.WriteAllBytesAsync(tempDatabase, snapshot);
            CryptographicOperations.ZeroMemory(snapshot);

            var dbKeys = new HashSet<string>(StringComparer.Ordinal);
            var accountPresent = false;
            var accountCount = 0;
            var connectionCount = 0;
            var observationCount = 0;
            var credentialReferenceCount = 0;
            await using (var connection = CreateBackupConnection(tempDatabase))
            {
                await connection.OpenAsync();
                accountPresent = await TableExistsAsync(connection, "AccountProfiles")
                    && await ExistsAsync(connection, "SELECT 1 FROM AccountProfiles WHERE lower(replace(Id, '-', '')) = $accountId LIMIT 1;", accountId);
                if (accountPresent)
                {
                    var connectionIds = await ReadGuidColumnAsync(
                        connection,
                        "SELECT Id FROM ProviderConnections WHERE lower(replace(AccountId, '-', '')) = $accountId;",
                        accountId,
                        "ProviderConnections");
                    foreach (var connectionId in connectionIds)
                    {
                        dbKeys.Add(SqliteAccountCredentialUiPort.GetVaultKey(connectionId));
                        dbKeys.Add(OAuthTokenVaultStore.GetAccessKey(connectionId));
                        dbKeys.Add(OAuthTokenVaultStore.GetRefreshKey(connectionId));
                        dbKeys.Add(OAuthTokenVaultStore.GetIdTokenKey(connectionId));
                    }

                    foreach (var key in await ReadTextColumnAsync(connection,
                                 "SELECT r.VaultKey FROM ProviderCredentialReferences r JOIN ProviderConnections c ON c.Id = r.ConnectionId WHERE lower(replace(c.AccountId, '-', '')) = $accountId;",
                                 accountId,
                                 "ProviderCredentialReferences"))
                    {
                        dbKeys.Add(key);
                    }

                    foreach (var key in await ReadTokenVaultKeysAsync(connection, accountId))
                    {
                        dbKeys.Add(key);
                    }

                    await DeleteConnectionRowsAsync(connection, "ProviderObservations", accountId);
                    await DeleteConnectionRowsAsync(connection, "ProviderCheckRuns", accountId);
                    await DeleteConnectionRowsAsync(connection, "ConnectionChecks", accountId);
                    await DeleteConnectionRowsAsync(connection, "ProviderSyncStates", accountId);
                    await DeleteConnectionRowsAsync(connection, "ProviderCredentialReferences", accountId);
                    await DeleteConnectionRowsAsync(connection, "TokenVaultReferences", accountId);
                    await DeleteAccountRowsAsync(connection, "ConnectionSessions", accountId);
                    await DeleteAccountRowsAsync(connection, "OAuthRegistrations", accountId);
                    await DeleteAccountRowsAsync(connection, "BrowserProfileBindings", accountId);
                    await DeleteAccountRowsAsync(connection, "ActivityEvents", accountId);
                    await DeleteAccountRowsAsync(connection, "ProviderAccountObservations", accountId);
                    await DeleteAccountRowsAsync(connection, "AccountTags", accountId);
                    await DeleteAccountRowsAsync(connection, "ProviderConnections", accountId);
                    await DeleteAccountProfileAsync(connection, accountId);

                    await ExecuteSqlAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
                    await ExecuteSqlAsync(connection, "VACUUM;");
                    accountCount = await CountTableAsync(connection, "AccountProfiles");
                    connectionCount = await CountTableAsync(connection, "ProviderConnections");
                    observationCount = await CountTableAsync(connection, "ProviderObservations");
                    credentialReferenceCount = await CountTableAsync(connection, "ProviderCredentialReferences");
                }
            }

            if (!accountPresent)
            {
                return false;
            }

            var backupVaultPath = Path.Combine(backupDirectory, PortableBackupFormat.VaultDirectoryName);
            if (Directory.Exists(backupVaultPath))
            {
                EnsureNoReparsePath(backupVaultPath);
                using var backupVault = new FileSecretVault(backupVaultPath, _dataProtectionProvider);
                foreach (var key in dbKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
                {
                    await backupVault.RemoveAsync(key);
                }

                using var backupAuthenticator = new DpapiAuthenticatorUiPort(backupVault, new Rfc6238TotpGenerator());
                var authenticatorResult = await backupAuthenticator.RemoveAccountCredentialsAsync(accountId);
                if (!authenticatorResult.IsSuccess)
                {
                    throw new BackupScrubException("BACKUP_TOTP_CLEANUP_FAILED", "Không thể dọn khóa 2FA khỏi một bản sao lưu; tài khoản hiện tại chưa bị xóa.");
                }
            }

            await using var snapshotConnection = CreateBackupConnection(tempDatabase, readOnly: true);
            await snapshotConnection.OpenAsync();
            var integrity = await ScalarStringAsync(snapshotConnection, "PRAGMA integrity_check;") ?? string.Empty;
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new BackupScrubException("BACKUP_INTEGRITY_FAILED", "Không thể xác nhận tính toàn vẹn của bản sao lưu sau khi dọn tài khoản.");
            }

            await snapshotConnection.CloseAsync();
            var cleanSnapshot = await File.ReadAllBytesAsync(tempDatabase);
            try
            {
                var replacement = _backupProtector.Protect(cleanSnapshot);
                try
                {
                    await WriteAtomicAsync(payloadPath, replacement);
                    var updatedManifest = manifest with
                    {
                        DatabasePayloadSizeBytes = replacement.Length,
                        DatabasePayloadSha256 = Convert.ToHexString(SHA256.HashData(replacement)).ToLowerInvariant(),
                        IntegrityStatus = integrity,
                        VaultFileCount = CountVaultFiles(backupVaultPath),
                        AccountCount = accountCount,
                        ProviderConnectionCount = connectionCount,
                        ObservationCount = observationCount,
                        CredentialReferenceCount = credentialReferenceCount
                    };
                    await WriteAtomicJsonAsync(manifestPath, updatedManifest);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(replacement);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(cleanSnapshot);
            }

            return true;
        }
        catch (BackupScrubException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SqliteException
            or CryptographicException
            or JsonException
            or SecretVaultException)
        {
            throw new BackupScrubException("BACKUP_SCRUB_FAILED", "Không thể dọn tài khoản khỏi một bản sao lưu cục bộ; dữ liệu tài khoản hiện tại được giữ để thử lại.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(snapshot);
            TryDeleteFile(tempDatabase);
            TryDeleteFile(tempDatabase + "-wal");
            TryDeleteFile(tempDatabase + "-shm");
        }
    }

    private static async Task<HashSet<string>> ReadTokenVaultKeysAsync(SqliteConnection connection, Guid accountId)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (!await TableExistsAsync(connection, "TokenVaultReferences")
            || !await TableExistsAsync(connection, "ProviderConnections")
            || !await ColumnExistsAsync(connection, "ProviderConnections", "AccountId"))
        {
            return keys;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.AccessTokenKey, r.RefreshTokenKey, r.IdTokenKey FROM TokenVaultReferences r JOIN ProviderConnections c ON c.Id = r.ConnectionId WHERE lower(replace(c.AccountId, '-', '')) = $accountId;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            for (var ordinal = 0; ordinal < 3; ordinal++)
            {
                if (!reader.IsDBNull(ordinal))
                {
                    keys.Add(reader.GetString(ordinal));
                }
            }
        }

        return keys;
    }

    private static async Task<IReadOnlyList<Guid>> ReadGuidColumnAsync(
        SqliteConnection connection,
        string sql,
        Guid accountId,
        string table)
    {
        if (!await TableExistsAsync(connection, table)
            || !await ColumnExistsAsync(connection, table, "Id")
            || !await ColumnExistsAsync(connection, table, "AccountId"))
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$accountId", accountId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<Guid>();
        while (await reader.ReadAsync())
        {
            if (Guid.TryParse(reader.GetString(0), out var value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> ReadTextColumnAsync(
        SqliteConnection connection,
        string sql,
        Guid accountId,
        string table)
    {
        if (!await TableExistsAsync(connection, table)
            || !await TableExistsAsync(connection, "ProviderConnections")
            || !await ColumnExistsAsync(connection, "ProviderConnections", "AccountId"))
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$accountId", accountId.ToString("N"));
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
            {
                result.Add(reader.GetString(0));
            }
        }

        return result;
    }

    private static async Task DeleteConnectionRowsAsync(SqliteConnection connection, string table, Guid accountId)
    {
        if (!await TableExistsAsync(connection, table)
            || !await ColumnExistsAsync(connection, table, "ConnectionId")
            || !await TableExistsAsync(connection, "ProviderConnections")
            || !await ColumnExistsAsync(connection, "ProviderConnections", "AccountId"))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM \"{table}\" WHERE ConnectionId IN (SELECT Id FROM ProviderConnections WHERE lower(replace(AccountId, '-', '')) = $accountId);";
        command.Parameters.AddWithValue("$accountId", accountId.ToString("N"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteAccountRowsAsync(SqliteConnection connection, string table, Guid accountId)
    {
        if (!await TableExistsAsync(connection, table) || !await ColumnExistsAsync(connection, table, "AccountId"))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM \"{table}\" WHERE lower(replace(AccountId, '-', '')) = $accountId;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString("N"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteAccountProfileAsync(SqliteConnection connection, Guid accountId)
    {
        if (!await TableExistsAsync(connection, "AccountProfiles")
            || !await ColumnExistsAsync(connection, "AccountProfiles", "Id"))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AccountProfiles WHERE lower(replace(Id, '-', '')) = $accountId;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString("N"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> ExistsAsync(SqliteConnection connection, string sql, Guid accountId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$accountId", accountId.ToString("N"));
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<int> CountTableAsync(SqliteConnection connection, string table)
    {
        if (!await TableExistsAsync(connection, table))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteSqlAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() as string;
    }

    private static SqliteConnection CreateBackupConnection(string path, bool readOnly = false)
        => new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString());

    private static int CountVaultFiles(string path)
        => Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.secret", SearchOption.TopDirectoryOnly).Count()
            : 0;

    private static void EnsureRegularFile(string path)
    {
        if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new BackupScrubException("BACKUP_PATH_INVALID", "Đường dẫn bản sao lưu không hợp lệ; chưa xóa tài khoản.");
        }
    }

    private static void EnsureNoReparsePath(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if ((current.Exists || File.Exists(current.FullName))
                && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new BackupScrubException("BACKUP_PATH_INVALID", "Thư mục sao lưu có liên kết hệ thống tệp; chưa xóa tài khoản.");
            }

            current = current.Parent;
        }
    }

    private static async Task WriteAtomicAsync(string targetPath, byte[] bytes)
    {
        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static async Task WriteAtomicJsonAsync<T>(string targetPath, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            await WriteAtomicAsync(targetPath, bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task CompactDatabaseBestEffortAsync(FlashAiDbContext context)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", CancellationToken.None);
            await context.Database.ExecuteSqlRawAsync("VACUUM;", CancellationToken.None);
        }
        catch (SqliteException)
        {
            // Account state and vault have already been removed. SQLite compaction is best effort;
            // secure_delete was enabled on the connection before the purge transaction.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class BackupScrubException : Exception
    {
        public BackupScrubException(string errorCode, string message, Exception? inner = null)
            : base(message, inner)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
