using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Authenticator.Contracts;
using FlashAI.Modules.Authenticator.Integration;

namespace FlashAI.Modules.Authenticator.Application;

public sealed class DpapiAuthenticatorUiPort : IAuthenticatorUiPort, IDisposable
{
    private const string CredentialIndexKey = "authenticator/credentials/index";
    private const string PassphraseVerifierKey = "authenticator/vault/verifier";
    private const int PassphraseIterations = 120_000;
    private static readonly SemaphoreSlim IndexGate = new(1, 1);
    private readonly ISecretVault _vault;
    private readonly ITotpGenerator _totpGenerator;
    private readonly IVaultAccessState? _accessState;
    private bool _isUnlocked;
    private bool _disposed;

    public DpapiAuthenticatorUiPort(ISecretVault vault, ITotpGenerator totpGenerator, IVaultAccessState? accessState = null)
    {
        _vault = vault;
        _totpGenerator = totpGenerator;
        _accessState = accessState;
    }

    public Task<bool> IsVaultLockedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_disposed || !_isUnlocked);
    }

    public async Task<OperationResultVm> UnlockVaultAsync(
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length is < 8 or > 256)
        {
            return OperationResultVm.Failure(
                "INVALID_PASSPHRASE",
                "The unlock passphrase must contain between 8 and 256 characters.");
        }

        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var verifierPayload = await _vault.ReadAsync(PassphraseVerifierKey, cancellationToken);
            if (verifierPayload is null)
            {
                var verifier = CreateVerifier(passphrase);
                try
                {
                    await _vault.StoreAsync(PassphraseVerifierKey, verifier, cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(verifier);
                }

                _isUnlocked = true;
                _accessState?.SetUnlocked(true);
                return OperationResultVm.Success("The encrypted local vault is initialized and unlocked.");
            }

            var isValid = VerifyPassphrase(passphrase, verifierPayload.Value.Span);
            _isUnlocked = isValid;
            _accessState?.SetUnlocked(isValid);
            return isValid
                ? OperationResultVm.Success("The encrypted local vault is unlocked.")
                : OperationResultVm.Failure("INVALID_PASSPHRASE", "The unlock passphrase is incorrect.");
        }
        catch (SecretVaultException exception)
        {
            _isUnlocked = false;
            return OperationResultVm.Failure(exception.ErrorCode, exception.Message);
        }
        finally
        {
            IndexGate.Release();
        }
    }

    public Task<OperationResultVm> LockVaultAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _isUnlocked = false;
        _accessState?.SetUnlocked(false);
        return Task.FromResult(OperationResultVm.Success("The encrypted local vault is locked."));
    }

    public async Task<IReadOnlyList<TotpCredentialVm>> GetCredentialsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return [];
        }

        if (!_isUnlocked || accountId == Guid.Empty)
        {
            return [];
        }

        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var index = await ReadIndexAsync(cancellationToken);
            return index
                .Where(item => item.AccountId == accountId)
                .Select(item => new TotpCredentialVm(
                    item.CredentialId,
                    item.AccountId,
                    item.Issuer,
                    item.AccountLabel,
                    item.MaskedSecret,
                    item.Digits,
                    item.Period,
                    IsDemo: false))
                .ToArray();
        }
        finally
        {
            IndexGate.Release();
        }
    }

    public async Task<TotpCodeVm?> GetCurrentCodeAsync(
        Guid credentialId,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return null;
        }

        if (!_isUnlocked || credentialId == Guid.Empty)
        {
            return null;
        }

        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var index = await ReadIndexAsync(cancellationToken);
            var metadata = index.FirstOrDefault(item => item.CredentialId == credentialId);
            if (metadata is null)
            {
                return null;
            }

            var payload = await _vault.ReadAsync(GetSecretKey(credentialId), cancellationToken);
            if (payload is null)
            {
                return null;
            }

            var secret = DeserializeSecret(payload.Value.Span);
            if (secret.CredentialId != credentialId || secret.AccountId != metadata.AccountId)
            {
                return null;
            }

            var decodedSecret = Convert.FromBase64String(secret.NormalizedSecret);
            try
            {
                var now = DateTimeOffset.UtcNow;
                var remaining = secret.Period - (int)(now.ToUnixTimeSeconds() % secret.Period);
                var code = _totpGenerator.Generate(decodedSecret, now, secret.Digits, secret.Period, secret.Algorithm);
                return new TotpCodeVm(code, remaining, IsDemo: false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decodedSecret);
            }
        }
        catch (FormatException)
        {
            return null;
        }
        finally
        {
            IndexGate.Release();
        }
    }

    public async Task<OperationResultVm> AddCredentialAsync(
        Guid accountId,
        string issuer,
        string label,
        string maskedOrDemoSeed,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return OperationResultVm.Failure("AUTHENTICATOR_DISPOSED", "The authenticator session has been disposed.");
        }

        if (!_isUnlocked)
        {
            return OperationResultVm.Failure("VAULT_LOCKED", "Unlock the encrypted local vault before adding a credential.");
        }

        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(label) || label.Length > 256)
        {
            return OperationResultVm.Failure("INVALID_CREDENTIAL_METADATA", "The account and credential label are required.");
        }

        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 128)
        {
            return OperationResultVm.Failure("INVALID_CREDENTIAL_METADATA", "The issuer is required and must be 128 characters or fewer.");
        }

        string normalizedSecret;
        try
        {
            normalizedSecret = Base32SecretDecoder.Normalize(maskedOrDemoSeed);
        }
        catch (FormatException)
        {
            return OperationResultVm.Failure("INVALID_TOTP_SECRET", "The TOTP secret is not valid Base32.");
        }

        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var index = await ReadIndexAsync(cancellationToken);
            if (index.Any(item => item.AccountId == accountId
                && item.Issuer.Equals(issuer.Trim(), StringComparison.OrdinalIgnoreCase)
                && item.AccountLabel.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResultVm.Failure("DUPLICATE_CREDENTIAL", "A credential with the same issuer and label already exists.");
            }

            var credentialId = Guid.NewGuid();
            var metadata = new CredentialMetadata(
                credentialId,
                accountId,
                issuer.Trim(),
                label.Trim(),
                MaskSecret(normalizedSecret),
                6,
                30,
                DateTimeOffset.UtcNow);
            var secret = new TotpSecret(
                credentialId,
                accountId,
                normalizedSecret,
                6,
                30,
                TotpAlgorithm.Sha1);

            var serializedSecret = JsonSerializer.SerializeToUtf8Bytes(secret);
            try
            {
                await _vault.StoreAsync(GetSecretKey(credentialId), serializedSecret, cancellationToken);
                index.Add(metadata);
                await SaveIndexAsync(index, cancellationToken);
            }
            catch
            {
                await _vault.RemoveAsync(GetSecretKey(credentialId), CancellationToken.None);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(serializedSecret);
            }

            return OperationResultVm.Success("The TOTP credential was stored in the encrypted local vault.");
        }
        catch (SecretVaultException exception)
        {
            return OperationResultVm.Failure(exception.ErrorCode, exception.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(Convert.FromBase64String(normalizedSecret));
            IndexGate.Release();
        }
    }

    public async Task<OperationResultVm> RemoveCredentialAsync(
        Guid credentialId,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return OperationResultVm.Failure("AUTHENTICATOR_DISPOSED", "The authenticator session has been disposed.");
        }

        if (!_isUnlocked)
        {
            return OperationResultVm.Failure("VAULT_LOCKED", "Unlock the encrypted local vault before removing a credential.");
        }

        await IndexGate.WaitAsync(cancellationToken);
        try
        {
            var index = await ReadIndexAsync(cancellationToken);
            var removed = index.RemoveAll(item => item.CredentialId == credentialId);
            if (removed == 0)
            {
                return OperationResultVm.Failure("CREDENTIAL_NOT_FOUND", "The credential was not found.");
            }

            await _vault.RemoveAsync(GetSecretKey(credentialId), cancellationToken);
            await SaveIndexAsync(index, cancellationToken);
            return OperationResultVm.Success("The TOTP credential was removed from the encrypted local vault.");
        }
        catch (SecretVaultException exception)
        {
            return OperationResultVm.Failure(exception.ErrorCode, exception.Message);
        }
        finally
        {
            IndexGate.Release();
        }
    }

    private async Task<List<CredentialMetadata>> ReadIndexAsync(CancellationToken cancellationToken)
    {
        var payload = await _vault.ReadAsync(CredentialIndexKey, cancellationToken);
        if (payload is null)
        {
            return [];
        }

        var index = JsonSerializer.Deserialize<List<CredentialMetadata>>(payload.Value.Span);
        return index ?? [];
    }

    private Task SaveIndexAsync(List<CredentialMetadata> index, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(index);
        return StoreAndClearAsync(CredentialIndexKey, payload, cancellationToken);
    }

    private async Task StoreAndClearAsync(string key, byte[] payload, CancellationToken cancellationToken)
    {
        try
        {
            await _vault.StoreAsync(key, payload, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static byte[] CreateVerifier(string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                passphraseBytes,
                salt,
                PassphraseIterations,
                HashAlgorithmName.SHA256,
                32);
            var verifier = new PassphraseVerifier(Convert.ToBase64String(salt), PassphraseIterations, Convert.ToBase64String(hash));
            CryptographicOperations.ZeroMemory(hash);
            return JsonSerializer.SerializeToUtf8Bytes(verifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(passphraseBytes);
        }
    }

    private static bool VerifyPassphrase(string passphrase, ReadOnlySpan<byte> payload)
    {
        try
        {
            var verifier = JsonSerializer.Deserialize<PassphraseVerifier>(payload);
            if (verifier is null || verifier.Iterations < 100_000)
            {
                return false;
            }

            var salt = Convert.FromBase64String(verifier.SaltBase64);
            var expected = Convert.FromBase64String(verifier.HashBase64);
            var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
            try
            {
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    passphraseBytes,
                    salt,
                    verifier.Iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);
                try
                {
                    return CryptographicOperations.FixedTimeEquals(actual, expected);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(actual);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(passphraseBytes);
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static TotpSecret DeserializeSecret(ReadOnlySpan<byte> payload)
        => JsonSerializer.Deserialize<TotpSecret>(payload)
            ?? throw new FormatException("The TOTP secret payload is invalid.");

    private static string GetSecretKey(Guid credentialId) => $"authenticator/totp/{credentialId:N}";

    private static string MaskSecret(string normalizedSecret)
    {
        var preview = normalizedSecret.Length <= 8
            ? "********"
            : normalizedSecret[..4] + "********" + normalizedSecret[^4..];
        return preview;
    }

    private sealed record PassphraseVerifier(string SaltBase64, int Iterations, string HashBase64);

    private sealed record CredentialMetadata(
        Guid CredentialId,
        Guid AccountId,
        string Issuer,
        string AccountLabel,
        string MaskedSecret,
        int Digits,
        int Period,
        DateTimeOffset CreatedAtUtc);

    public void Dispose()
    {
        _isUnlocked = false;
        _accessState?.SetUnlocked(false);
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record TotpSecret(
        Guid CredentialId,
        Guid AccountId,
        string NormalizedSecret,
        int Digits,
        int Period,
        TotpAlgorithm Algorithm = TotpAlgorithm.Sha1);
}
