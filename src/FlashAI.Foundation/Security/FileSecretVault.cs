using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;

namespace FlashAI.Foundation.Security;

public sealed class FileSecretVault : ISecretVault, IDisposable
{
    private const int MaximumKeyLength = 256;
    private const int CurrentEnvelopeVersion = 1;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DirectoryGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _vaultDirectory;
    private readonly string _lockPath;
    private readonly IDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public FileSecretVault(string vaultDirectory, IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultDirectory);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _vaultDirectory = Path.GetFullPath(vaultDirectory);
        _lockPath = Path.Combine(_vaultDirectory, ".vault.lock");
        _protector = dataProtectionProvider.CreateProtector(
            "FLASH ROUTER",
            "local-secret-vault",
            "v1");
    }

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            EnsureVaultDirectory();
            return ValueTask.FromResult(Directory.Exists(_vaultDirectory));
        }
        catch (SecretVaultException)
        {
            return ValueTask.FromResult(false);
        }
        catch (IOException)
        {
            return ValueTask.FromResult(false);
        }
        catch (UnauthorizedAccessException)
        {
            return ValueTask.FromResult(false);
        }
    }

    public async ValueTask StoreAsync(
        string key,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken);
        IDisposable? fileLock = null;
        try
        {
            fileLock = await AcquireDirectoryLockAsync(cancellationToken);
            var path = GetSecretPath(key);
            var current = await ReadEnvelopeAsync(path, key, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var envelope = new SecretEnvelope(
                CurrentEnvelopeVersion,
                (current?.Revision ?? 0) + 1,
                current?.CreatedAtUtc ?? now,
                now,
                Convert.ToBase64String(value.Span));

            var serialized = JsonSerializer.SerializeToUtf8Bytes(envelope);
            byte[]? protectedPayload = null;
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                protectedPayload = GetEntryProtector(key).Protect(serialized);
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    options: FileOptions.WriteThrough | FileOptions.SequentialScan))
                {
                    await stream.WriteAsync(protectedPayload, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(serialized);
                if (protectedPayload is not null)
                {
                    CryptographicOperations.ZeroMemory(protectedPayload);
                }

                TryDeleteTemporaryFile(temporaryPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecretVaultException)
        {
            throw;
        }
        catch (CryptographicException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_PROTECT_FAILED",
                "The local secret vault could not protect the value.",
                exception);
        }
        catch (IOException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_IO_FAILED",
                "The local secret vault could not write the value.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_ACCESS_FAILED",
                "The local secret vault could not write the value.",
                exception);
        }
        finally
        {
            fileLock?.Dispose();
            _gate.Release();
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken);
        IDisposable? fileLock = null;
        try
        {
            fileLock = await AcquireDirectoryLockAsync(cancellationToken);
            return await ReadValueAsync(GetSecretPath(key), key, cancellationToken);
        }
        finally
        {
            fileLock?.Dispose();
            _gate.Release();
        }
    }

    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken);
        IDisposable? fileLock = null;
        try
        {
            fileLock = await AcquireDirectoryLockAsync(cancellationToken);
            var path = GetSecretPath(key);
            EnsureRegularPath(path, allowMissing: true);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_IO_FAILED",
                "The local secret vault could not remove the value.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_ACCESS_FAILED",
                "The local secret vault could not remove the value.",
                exception);
        }
        finally
        {
            fileLock?.Dispose();
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _gate.Dispose();
    }

    private async Task<IDisposable> AcquireDirectoryLockAsync(CancellationToken cancellationToken)
    {
        EnsureVaultDirectory();
        var directoryGate = DirectoryGates.GetOrAdd(
            _vaultDirectory,
            static _ => new SemaphoreSlim(1, 1));
        await directoryGate.WaitAsync(cancellationToken);

        try
        {
            EnsureVaultDirectory();
            EnsureRegularPath(_lockPath, allowMissing: true);
            var stream = new FileStream(
                _lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.WriteThrough);
            try
            {
                EnsureRegularPath(_lockPath, allowMissing: false);
                return new DirectoryLockLease(stream, directoryGate);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch
        {
            directoryGate.Release();
            throw;
        }
    }

    private void EnsureVaultDirectory()
    {
        try
        {
            Directory.CreateDirectory(_vaultDirectory);
            EnsureNoReparseComponents(_vaultDirectory);
            EnsureRegularPath(_vaultDirectory, allowMissing: false, requireDirectory: true);
        }
        catch (SecretVaultException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_IO_FAILED",
                "The local secret vault directory could not be opened.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_ACCESS_FAILED",
                "The local secret vault directory could not be opened.",
                exception);
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

            var attributes = File.GetAttributes(current);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new SecretVaultException(
                    "SECRET_VAULT_PATH_INVALID",
                    "The local secret vault path cannot use a reparse point.");
            }
        }
    }

    private static void EnsureRegularPath(
        string path,
        bool allowMissing,
        bool requireDirectory = false)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)
                || (requireDirectory && !attributes.HasFlag(FileAttributes.Directory))
                || (!requireDirectory && attributes.HasFlag(FileAttributes.Directory)))
            {
                throw new SecretVaultException(
                    "SECRET_VAULT_PATH_INVALID",
                    "The local secret vault path is not a regular file or directory.");
            }
        }
        catch (FileNotFoundException) when (allowMissing)
        {
        }
        catch (DirectoryNotFoundException) when (allowMissing)
        {
        }
    }

    private async Task<ReadOnlyMemory<byte>?> ReadValueAsync(
        string path,
        string key,
        CancellationToken cancellationToken)
    {
        EnsureRegularPath(path, allowMissing: true);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var envelope = await ReadEnvelopeAsync(path, key, cancellationToken);
            if (envelope is null)
            {
                return null;
            }

            return Convert.FromBase64String(envelope.ValueBase64);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FormatException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_CORRUPT",
                "The local secret vault contains an invalid value.",
                exception);
        }
        catch (CryptographicException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_UNAVAILABLE",
                "The local secret vault could not decrypt the value for this Windows user.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_CORRUPT",
                "The local secret vault contains an invalid value.",
                exception);
        }
        catch (IOException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_IO_FAILED",
                "The local secret vault could not read the value.",
                exception);
        }
    }

    private async Task<SecretEnvelope?> ReadEnvelopeAsync(
        string path,
        string key,
        CancellationToken cancellationToken)
    {
        EnsureRegularPath(path, allowMissing: true);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] protectedPayload;
        try
        {
            protectedPayload = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_ACCESS_FAILED",
                "The local secret vault could not read the value.",
                exception);
        }
        byte[]? serialized = null;
        try
        {
            serialized = GetEntryProtector(key).Unprotect(protectedPayload);
            var envelope = JsonSerializer.Deserialize<SecretEnvelope>(serialized);
            if (envelope is null
                || envelope.Version != CurrentEnvelopeVersion
                || envelope.Revision < 1
                || string.IsNullOrEmpty(envelope.ValueBase64)
                || envelope.UpdatedAtUtc < envelope.CreatedAtUtc)
            {
                throw new SecretVaultException(
                    "SECRET_VAULT_CORRUPT",
                    "The local secret vault contains an invalid value.");
            }

            return envelope;
        }
        catch (SecretVaultException)
        {
            throw;
        }
        catch (FormatException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_CORRUPT",
                "The local secret vault contains an invalid value.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_CORRUPT",
                "The local secret vault contains an invalid value.",
                exception);
        }
        catch (CryptographicException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_UNAVAILABLE",
                "The local secret vault could not decrypt the value for this Windows user.",
                exception);
        }
        catch (IOException exception)
        {
            throw new SecretVaultException(
                "SECRET_VAULT_IO_FAILED",
                "The local secret vault could not read the value.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedPayload);
            if (serialized is not null)
            {
                CryptographicOperations.ZeroMemory(serialized);
            }
        }
    }

    private string GetSecretPath(string key)
    {
        var digest = GetKeyDigest(key);
        return Path.Combine(_vaultDirectory, Convert.ToHexString(digest).ToLowerInvariant() + ".secret");
    }

    private IDataProtector GetEntryProtector(string key)
    {
        var digest = GetKeyDigest(key);
        try
        {
            return _protector.CreateProtector("entry", Convert.ToHexString(digest));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private static byte[] GetKeyDigest(string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        try
        {
            return SHA256.HashData(keyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaximumKeyLength || key.Any(char.IsControl))
        {
            throw new ArgumentException("Secret vault keys must be non-empty, bounded, and contain no control characters.", nameof(key));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void TryDeleteTemporaryFile(string path)
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
            // The primary operation already failed; do not replace its safe error.
        }
        catch (UnauthorizedAccessException)
        {
            // The primary operation already failed; do not replace its safe error.
        }
    }

    private sealed record SecretEnvelope(
        int Version,
        int Revision,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string ValueBase64);

    private sealed class DirectoryLockLease : IDisposable
    {
        private readonly FileStream _stream;
        private readonly SemaphoreSlim _gate;
        private int _disposed;

        public DirectoryLockLease(FileStream stream, SemaphoreSlim gate)
        {
            _stream = stream;
            _gate = gate;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stream.Dispose();
            _gate.Release();
        }
    }
}
