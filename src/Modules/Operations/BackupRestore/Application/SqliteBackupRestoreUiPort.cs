using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.BackupRestore.Contracts;
using FlashAI.Modules.BackupRestore.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace FlashAI.Modules.BackupRestore.Application;

public sealed class SqliteBackupRestoreUiPort : IBackupRestoreUiPort
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly LocalDataPaths _paths;
    private readonly SqliteConnectionFactory _connections;
    private readonly IDataProtector _protector;

    public SqliteBackupRestoreUiPort(
        LocalDataPaths paths,
        SqliteConnectionFactory connections,
        IDataProtectionProvider dataProtectionProvider)
    {
        _paths = paths;
        _connections = connections;
        _protector = dataProtectionProvider.CreateProtector(
            "FLASH ROUTER",
            "local-backup",
            PortableBackupFormat.CurrentVersion);
    }

    public async Task<IReadOnlyList<BackupItemVm>> GetBackupsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_paths.Backups))
        {
            return [];
        }

        var items = new List<BackupItemVm>();
        foreach (var directory in Directory.EnumerateDirectories(_paths.Backups))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileName(directory), out _))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, PortableBackupFormat.ManifestFileName);
            try
            {
                EnsureRegularPath(directory, requireDirectory: true);
                EnsureRegularPath(manifestPath);
                var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
                var payloadPath = Path.Combine(directory, PortableBackupFormat.DatabasePayloadFileName);
                EnsureRegularPath(payloadPath);
                items.Add(new BackupItemVm(
                    manifest.BackupId,
                    manifest.DisplayName,
                    manifest.CreatedAtUtc,
                    FormatBytes(manifest.DatabasePayloadSizeBytes),
                    manifest.BackupType,
                    manifest.EncryptionStatus == "DataProtection-DPAPI",
                    manifest.IntegrityStatus));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or BackupValidationException)
            {
                if (Guid.TryParse(Path.GetFileName(directory), out var backupId))
                {
                    items.Add(new BackupItemVm(
                        backupId,
                        "Bản sao lưu không đọc được",
                        DateTimeOffset.MinValue,
                        "Không xác định",
                        "Không xác định",
                        false,
                        "Corrupted"));
                }
            }
        }

        return items
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    public async Task<OperationResultVm> RequestCreateBackupAsync(
        string name,
        string type,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateDisplayName(name, out var displayName))
        {
            return OperationResultVm.Failure(
                "BACKUP_NAME_INVALID",
                "Tên bản sao lưu không hợp lệ hoặc chứa ký tự đường dẫn.");
        }

        if (!string.Equals(type, "Toàn phần", StringComparison.Ordinal))
        {
            return OperationResultVm.Failure(
                "BACKUP_TYPE_UNSUPPORTED",
                "BE-06 chỉ tạo bản sao lưu toàn phần nhất quán gồm SQLite và ciphertext của vault.");
        }

        try
        {
            var manifest = await CreateBackupAsync(displayName, type, cancellationToken);
            return OperationResultVm.Success(
                $"Đã tạo bản sao lưu được mã hóa ({manifest.BackupId:N}); có thể kiểm tra khôi phục trên dữ liệu tạm.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BackupValidationException exception)
        {
            return OperationResultVm.Failure(exception.ErrorCode, exception.Message);
        }
        catch (FormatException)
        {
            return OperationResultVm.Failure("BACKUP_MANIFEST_INVALID", "Manifest hoặc checksum của bản sao lưu không hợp lệ.");
        }
        catch (IOException)
        {
            return OperationResultVm.Failure("BACKUP_IO_FAILED", "Không thể ghi bản sao lưu cục bộ.");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResultVm.Failure("BACKUP_ACCESS_FAILED", "Không thể truy cập vùng dữ liệu sao lưu cục bộ.");
        }
        catch (SqliteException)
        {
            return OperationResultVm.Failure("BACKUP_SQLITE_FAILED", "Không thể tạo snapshot SQLite nhất quán.");
        }
    }

    public async Task<OperationResultVm> RequestRestoreAsync(
        Guid backupId,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            return OperationResultVm.Failure(
                "RESTORE_CONFIRMATION_REQUIRED",
                "Khôi phục chỉ được thực hiện như một bài kiểm tra trên thư mục dữ liệu tạm; hãy xác nhận thao tác.");
        }

        try
        {
            var report = await ValidateRestoreAsync(backupId, cancellationToken);
            return OperationResultVm.Success(
                $"Kiểm tra khôi phục đạt: integrity={report.IntegrityStatus}, schema={report.SchemaVersion}; database đang sử dụng không bị thay đổi.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BackupValidationException exception)
        {
            return OperationResultVm.Failure(exception.ErrorCode, exception.Message);
        }
        catch (IOException)
        {
            return OperationResultVm.Failure("RESTORE_IO_FAILED", "Không thể đọc hoặc tạo workspace kiểm tra khôi phục.");
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResultVm.Failure("RESTORE_ACCESS_FAILED", "Không thể truy cập workspace kiểm tra khôi phục.");
        }
        catch (SqliteException)
        {
            return OperationResultVm.Failure("RESTORE_SQLITE_FAILED", "Snapshot khôi phục không mở được như một cơ sở dữ liệu SQLite.");
        }
    }

    private async Task<BackupManifest> CreateBackupAsync(
        string displayName,
        string backupType,
        CancellationToken cancellationToken)
    {
        await BackupMaintenanceGate.Semaphore.WaitAsync(cancellationToken);
        var backupId = Guid.NewGuid();
        var backupDirectory = Path.Combine(_paths.Backups, backupId.ToString("N"));
        var tempDirectory = Path.Combine(_paths.Temp, "backup-" + backupId.ToString("N"));
        var tempDatabase = Path.Combine(tempDirectory, "database.sqlite");
        var payloadPath = Path.Combine(backupDirectory, PortableBackupFormat.DatabasePayloadFileName);
        var manifestPath = Path.Combine(backupDirectory, PortableBackupFormat.ManifestFileName);

        try
        {
            Directory.CreateDirectory(_paths.Backups);
            Directory.CreateDirectory(_paths.Temp);
            EnsureRegularPath(_paths.Backups, requireDirectory: true);
            EnsureRegularPath(_paths.Temp, requireDirectory: true);
            Directory.CreateDirectory(tempDirectory);
            EnsureRegularPath(tempDirectory, requireDirectory: true);

            if (!File.Exists(_paths.DatabaseFile))
            {
                throw new BackupValidationException("BACKUP_DATABASE_MISSING", "Cơ sở dữ liệu SQLite hiện tại không tồn tại.");
            }

            var sourceDatabaseId = ComputeStableIdentifier(_paths.DatabaseFile);
            var snapshot = await CreateSqliteSnapshotAsync(tempDatabase, cancellationToken);
            try
            {
                if (!string.Equals(snapshot.IntegrityStatus, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BackupValidationException("BACKUP_INTEGRITY_FAILED", "Snapshot SQLite không vượt qua integrity_check.");
                }

                var encryptedPayload = _protector.Protect(snapshot.Bytes);
                try
                {
                    Directory.CreateDirectory(backupDirectory);
                    EnsureRegularPath(backupDirectory, requireDirectory: true);
                    await WriteAtomicAsync(payloadPath, encryptedPayload, cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(encryptedPayload);
                }

                var vaultFileCount = CopyVaultCiphertext(backupDirectory);
                var manifest = new BackupManifest(
                    PortableBackupFormat.CurrentVersion,
                    backupId,
                    displayName,
                    backupType,
                    DateTimeOffset.UtcNow,
                    snapshot.SchemaVersion,
                    sourceDatabaseId,
                    new FileInfo(payloadPath).Length,
                    await ComputeSha256Async(payloadPath, cancellationToken),
                    snapshot.IntegrityStatus,
                    "DataProtection-DPAPI",
                    true,
                    vaultFileCount,
                    false,
                    false,
                    "Same Windows user/profile; disposable restore validation only",
                    snapshot.AccountCount,
                    snapshot.ProviderConnectionCount,
                    snapshot.ObservationCount,
                    snapshot.CredentialReferenceCount);

                await WriteAtomicJsonAsync(manifestPath, manifest, cancellationToken);
                return manifest;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(snapshot.Bytes);
            }
        }
        catch
        {
            TryDeleteDirectory(backupDirectory);
            throw;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
            BackupMaintenanceGate.Semaphore.Release();
        }
    }

    private async Task<RestoreReport> ValidateRestoreAsync(Guid backupId, CancellationToken cancellationToken)
    {
        await BackupMaintenanceGate.Semaphore.WaitAsync(cancellationToken);
        var restoreDirectory = Path.Combine(_paths.TestData, "be06-restore-" + Guid.NewGuid().ToString("N"));
        var restoreDatabase = Path.Combine(restoreDirectory, "database.sqlite");

        try
        {
            var backupDirectory = GetBackupDirectory(backupId);
            var manifestPath = Path.Combine(backupDirectory, PortableBackupFormat.ManifestFileName);
            var payloadPath = Path.Combine(backupDirectory, PortableBackupFormat.DatabasePayloadFileName);
            var vaultDirectory = Path.Combine(backupDirectory, PortableBackupFormat.VaultDirectoryName);
            EnsureRegularPath(backupDirectory, requireDirectory: true);
            EnsureRegularPath(manifestPath);
            EnsureRegularPath(payloadPath);
            var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
            if (manifest.BackupId != backupId
                || !string.Equals(manifest.FormatVersion, PortableBackupFormat.CurrentVersion, StringComparison.Ordinal)
                || !string.Equals(manifest.EncryptionStatus, "DataProtection-DPAPI", StringComparison.Ordinal))
            {
                throw new BackupValidationException("BACKUP_INCOMPATIBLE", "Bản sao lưu không tương thích với phiên bản hiện tại.");
            }

            var actualHash = await ComputeSha256Async(payloadPath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(manifest.DatabasePayloadSha256),
                    Convert.FromHexString(actualHash)))
            {
                throw new BackupValidationException("BACKUP_CHECKSUM_INVALID", "Checksum của bản sao lưu không khớp.");
            }

            var encryptedPayload = await File.ReadAllBytesAsync(payloadPath, cancellationToken);
            byte[] snapshot;
            try
            {
                snapshot = _protector.Unprotect(encryptedPayload);
            }
            catch (CryptographicException exception)
            {
                throw new BackupValidationException("BACKUP_ENCRYPTION_INVALID", "Không thể giải mã bản sao lưu bằng key ring hiện tại.", exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encryptedPayload);
            }

            try
            {
                Directory.CreateDirectory(_paths.TestData);
                Directory.CreateDirectory(restoreDirectory);
                EnsureRegularPath(restoreDirectory, requireDirectory: true);
                await WriteAtomicAsync(restoreDatabase, snapshot, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(snapshot);
            }

            var snapshotReport = await InspectRestoredDatabaseAsync(restoreDatabase, cancellationToken);
            try
            {
                if (!string.Equals(snapshotReport.IntegrityStatus, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BackupValidationException("RESTORE_INTEGRITY_FAILED", "Snapshot khôi phục không vượt qua integrity_check.");
                }

                var actualVaultFileCount = 0;
                if (Directory.Exists(vaultDirectory))
                {
                    EnsureRegularPath(vaultDirectory, requireDirectory: true);
                    foreach (var vaultFile in Directory.EnumerateFiles(vaultDirectory, "*.secret", SearchOption.TopDirectoryOnly))
                    {
                        EnsureRegularPath(vaultFile);
                        actualVaultFileCount++;
                    }
                }

                if (manifest.VaultFileCount != actualVaultFileCount)
                {
                    throw new BackupValidationException("RESTORE_VAULT_COUNT_MISMATCH", "Số ciphertext vault trong manifest không khớp với dữ liệu sao lưu.");
                }

                if (snapshotReport.CredentialReferenceCount > 0 && actualVaultFileCount == 0)
                {
                    throw new BackupValidationException("RESTORE_VAULT_REFERENCE_MISMATCH", "Bản sao lưu có credential reference nhưng không có ciphertext vault tương ứng.");
                }

                return new RestoreReport(snapshotReport.IntegrityStatus, snapshotReport.SchemaVersion, snapshotReport.CredentialReferenceCount);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(snapshotReport.Bytes);
            }
        }
        finally
        {
            TryDeleteDirectory(restoreDirectory);
            BackupMaintenanceGate.Semaphore.Release();
        }
    }

    private async Task<SqliteSnapshot> CreateSqliteSnapshotAsync(string tempDatabase, CancellationToken cancellationToken)
    {
        await using var source = new SqliteConnection(_connections.CreateConnectionString());
        await source.OpenAsync(cancellationToken);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = tempDatabase,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        cancellationToken.ThrowIfCancellationRequested();
        await destination.CloseAsync();

        return await InspectRestoredDatabaseAsync(tempDatabase, cancellationToken);
    }

    private static async Task<SqliteSnapshot> InspectRestoredDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);

        var integrity = await ScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
        var schema = await ScalarStringAsync(connection, "SELECT COALESCE((SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1), 'unknown');", cancellationToken);
        var accounts = await CountIfTableExistsAsync(connection, "AccountProfiles", cancellationToken);
        var connections = await CountIfTableExistsAsync(connection, "ProviderConnections", cancellationToken);
        var observations = await CountIfTableExistsAsync(connection, "ProviderObservations", cancellationToken);
        var references = await CountIfTableExistsAsync(connection, "ProviderCredentialReferences", cancellationToken);
        return new SqliteSnapshot(integrity, schema, accounts, connections, observations, references, await File.ReadAllBytesAsync(databasePath, cancellationToken));
    }

    private int CopyVaultCiphertext(string backupDirectory)
    {
        var sourceDirectory = Path.Combine(_paths.Security, "vault");
        var targetDirectory = Path.Combine(backupDirectory, PortableBackupFormat.VaultDirectoryName);
        Directory.CreateDirectory(targetDirectory);
        EnsureRegularPath(targetDirectory, requireDirectory: true);
        if (!Directory.Exists(sourceDirectory))
        {
            return 0;
        }

        EnsureRegularPath(sourceDirectory, requireDirectory: true);
        var count = 0;
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*.secret", SearchOption.TopDirectoryOnly))
        {
            EnsureRegularPath(source);
            var target = Path.Combine(targetDirectory, Path.GetFileName(source));
            File.Copy(source, target, overwrite: false);
            count++;
        }

        return count;
    }

    private string GetBackupDirectory(Guid backupId)
    {
        var directory = Path.Combine(_paths.Backups, backupId.ToString("N"));
        if (!Directory.Exists(directory))
        {
            throw new BackupValidationException("BACKUP_NOT_FOUND", "Không tìm thấy bản sao lưu được chọn.");
        }

        return directory;
    }

    private static async Task<BackupManifest> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, JsonOptions, cancellationToken);
        return manifest ?? throw new BackupValidationException("BACKUP_MANIFEST_INVALID", "Manifest của bản sao lưu không hợp lệ.");
    }

    private static async Task WriteAtomicJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            await WriteAtomicAsync(path, bytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task WriteAtomicAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string ComputeStableIdentifier(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant());
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
    }

    private static async Task<int> CountIfTableExistsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            return 0;
        }

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TryValidateDisplayName(string value, out string displayName)
    {
        displayName = value.Trim();
        return displayName.Length is > 0 and <= 100
            && displayName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !displayName.Contains(Path.DirectorySeparatorChar)
            && !displayName.Contains(Path.AltDirectorySeparatorChar)
            && !displayName.Any(char.IsControl);
    }

    private static void EnsureRegularPath(string path, bool requireDirectory = false)
    {
        EnsureNoReparseComponents(path);
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint)
            || (requireDirectory && !attributes.HasFlag(FileAttributes.Directory))
            || (!requireDirectory && attributes.HasFlag(FileAttributes.Directory)))
        {
            throw new BackupValidationException("BACKUP_PATH_INVALID", "Đường dẫn backup phải là file hoặc thư mục cục bộ thông thường.");
        }
    }

    private static void EnsureNoReparseComponents(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var current = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var remainder = fullPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in remainder)
        {
            current = string.IsNullOrEmpty(current) ? part : Path.Combine(current, part);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                continue;
            }

            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new BackupValidationException("BACKUP_PATH_INVALID", "Đường dẫn backup không được đi qua symbolic link hoặc reparse point.");
            }
        }
    }

    private static string FormatBytes(long bytes)
        => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024d:0.0} KB",
            _ => $"{bytes / (1024d * 1024d):0.0} MB"
        };

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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record SqliteSnapshot(
        string IntegrityStatus,
        string SchemaVersion,
        int AccountCount,
        int ProviderConnectionCount,
        int ObservationCount,
        int CredentialReferenceCount,
        byte[] Bytes);

    private sealed record RestoreReport(string IntegrityStatus, string SchemaVersion, int CredentialReferenceCount);

    private sealed class BackupValidationException : Exception
    {
        public BackupValidationException(string errorCode, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
