using System.Security.Cryptography;
using System.Text;
using FlashAI.Foundation.Observability;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Authenticator.Application;
using FlashAI.Modules.Authenticator.Features.CredentialImport.Application;
using FlashAI.Modules.Authenticator.Integration;
using Microsoft.AspNetCore.DataProtection;

namespace FlashAI.IntegrationTests;

public sealed class Be02SecurityTests
{
    [Fact]
    public async Task FileSecretVaultRoundTripsWithoutPlaintextCiphertext()
    {
        using var fixture = VaultFixture.Create();
        var plaintext = "synthetic-api-key-not-real";

        Assert.True(await fixture.Vault.IsAvailableAsync());
        await fixture.Vault.StoreAsync("provider/openai/api-key", Encoding.UTF8.GetBytes(plaintext));

        var files = Directory.GetFiles(fixture.VaultDirectory, "*.secret");
        Assert.Single(files);
        var ciphertext = Convert.ToBase64String(await File.ReadAllBytesAsync(files[0]));
        Assert.DoesNotContain(plaintext, ciphertext, StringComparison.Ordinal);

        var restored = await fixture.Vault.ReadAsync("provider/openai/api-key");
        Assert.NotNull(restored);
        Assert.Equal(plaintext, Encoding.UTF8.GetString(restored.Value.Span));

        await fixture.Vault.StoreAsync("provider/openai/api-key", Encoding.UTF8.GetBytes("synthetic-rotated-value"));
        var rotated = await fixture.Vault.ReadAsync("provider/openai/api-key");
        Assert.Equal("synthetic-rotated-value", Encoding.UTF8.GetString(rotated!.Value.Span));

        await fixture.Vault.RemoveAsync("provider/openai/api-key");
        Assert.Null(await fixture.Vault.ReadAsync("provider/openai/api-key"));
    }

    [Fact]
    public async Task FileSecretVaultRejectsAValueProtectedByAnotherKeyRing()
    {
        using var fixture = VaultFixture.Create();
        await fixture.Vault.StoreAsync("isolated/key", Encoding.UTF8.GetBytes("synthetic-secret"));

        var otherKeyRing = Path.Combine(fixture.Root, "other-keys");
        var otherProvider = DataProtectionProvider.Create(
            new DirectoryInfo(otherKeyRing),
            builder => builder.SetApplicationName("FLASH ROUTER OTHER TEST APP"));
        using var otherVault = new FileSecretVault(fixture.VaultDirectory, otherProvider);

        var exception = await Assert.ThrowsAsync<SecretVaultException>(async () =>
            await otherVault.ReadAsync("isolated/key"));

        Assert.Equal("SECRET_VAULT_UNAVAILABLE", exception.ErrorCode);
        Assert.DoesNotContain("synthetic-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileSecretVaultBindsCiphertextToItsLogicalKey()
    {
        using var fixture = VaultFixture.Create();
        await fixture.Vault.StoreAsync("owner/a", Encoding.UTF8.GetBytes("synthetic-owner-a"));
        await fixture.Vault.StoreAsync("owner/b", Encoding.UTF8.GetBytes("synthetic-owner-b"));

        var source = Path.Combine(fixture.VaultDirectory, VaultFixture.HashKey("owner/a") + ".secret");
        var target = Path.Combine(fixture.VaultDirectory, VaultFixture.HashKey("owner/b") + ".secret");
        var originalTarget = await File.ReadAllBytesAsync(target);
        try
        {
            File.Copy(source, target, overwrite: true);
            var exception = await Assert.ThrowsAsync<SecretVaultException>(async () =>
                await fixture.Vault.ReadAsync("owner/b"));
            Assert.Equal("SECRET_VAULT_UNAVAILABLE", exception.ErrorCode);
        }
        finally
        {
            await File.WriteAllBytesAsync(target, originalTarget);
        }
    }

    [Fact]
    public async Task FileSecretVaultCiphertextCanBeRestoredWithTheSameKeyRing()
    {
        using var fixture = VaultFixture.Create();
        await fixture.Vault.StoreAsync("backup/key", Encoding.UTF8.GetBytes("synthetic-backup-value"));

        var restoreDirectory = Path.Combine(fixture.Root, "restored-vault");
        Directory.CreateDirectory(restoreDirectory);
        var source = Path.Combine(fixture.VaultDirectory, VaultFixture.HashKey("backup/key") + ".secret");
        var target = Path.Combine(restoreDirectory, Path.GetFileName(source));
        File.Copy(source, target);

        var provider = DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(fixture.Root, "keys")),
            builder => builder.SetApplicationName("FLASH ROUTER BE-02 TEST"));
        using var restoredVault = new FileSecretVault(restoreDirectory, provider);
        var restored = await restoredVault.ReadAsync("backup/key");

        Assert.Equal("synthetic-backup-value", Encoding.UTF8.GetString(restored!.Value.Span));
    }

    [Fact]
    public async Task FileSecretVaultRoundTripsAfterProviderAndVaultRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "flash-router-be02-restart-" + Guid.NewGuid().ToString("N"));
        var keyRing = Path.Combine(root, "keys");
        var vaultDirectory = Path.Combine(root, "vault");
        try
        {
            var firstProvider = DataProtectionProvider.Create(
                new DirectoryInfo(keyRing),
                builder => builder.SetApplicationName("FLASH ROUTER BE-02 RESTART TEST"));
            using ((firstProvider as IDisposable)!)
            using (var firstVault = new FileSecretVault(vaultDirectory, firstProvider))
            {
                await firstVault.StoreAsync("restart/key", Encoding.UTF8.GetBytes("synthetic-restart-value"));
            }

            var secondProvider = DataProtectionProvider.Create(
                new DirectoryInfo(keyRing),
                builder => builder.SetApplicationName("FLASH ROUTER BE-02 RESTART TEST"));
            using ((secondProvider as IDisposable)!)
            using (var secondVault = new FileSecretVault(vaultDirectory, secondProvider))
            {
                var restored = await secondVault.ReadAsync("restart/key");
                Assert.Equal("synthetic-restart-value", Encoding.UTF8.GetString(restored!.Value.Span));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(59, "94287082")]
    [InlineData(1111111109, "07081804")]
    [InlineData(1111111111, "14050471")]
    [InlineData(1234567890, "89005924")]
    [InlineData(2000000000, "69279037")]
    public void Rfc6238Sha1VectorsAreImplemented(long unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        try
        {
            var generator = new Rfc6238TotpGenerator();
            var actual = generator.Generate(secret, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), 8, 30);
            Assert.Equal(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Theory]
    [InlineData(TotpAlgorithm.Sha256, 59, "46119246")]
    [InlineData(TotpAlgorithm.Sha256, 1111111109, "68084774")]
    [InlineData(TotpAlgorithm.Sha512, 59, "90693936")]
    [InlineData(TotpAlgorithm.Sha512, 1111111109, "25091201")]
    public void Rfc6238SupportedAlgorithmsMatchOfficialVectors(TotpAlgorithm algorithm, long unixSeconds, string expected)
    {
        var secret = algorithm == TotpAlgorithm.Sha256
            ? Encoding.ASCII.GetBytes("12345678901234567890123456789012")
            : Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");
        try
        {
            var generator = new Rfc6238TotpGenerator();
            Assert.Equal(expected, generator.Generate(secret, DateTimeOffset.FromUnixTimeSeconds(unixSeconds), 8, 30, algorithm));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    [Fact]
    public void Rfc6238RejectsInvalidSecretsClockDigitsAndPeriods()
    {
        var generator = new Rfc6238TotpGenerator();
        Assert.Throws<ArgumentException>(() => generator.Generate([], DateTimeOffset.UnixEpoch, 6, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate([1], DateTimeOffset.UnixEpoch, 5, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate([1], DateTimeOffset.UnixEpoch, 6, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate([1], DateTimeOffset.UnixEpoch.AddSeconds(-1), 6, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate([1], DateTimeOffset.UnixEpoch, 6, 30, (TotpAlgorithm)99));
    }

    [Fact]
    public async Task FileSecretVaultRejectsCorruptionAndPreservesAtomicConcurrentWrites()
    {
        using var fixture = VaultFixture.Create();
        await fixture.Vault.StoreAsync("corrupt/key", Encoding.UTF8.GetBytes("synthetic-value"));
        var path = Path.Combine(fixture.VaultDirectory, VaultFixture.HashKey("corrupt/key") + ".secret");
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[0] ^= 0x5a;
        await File.WriteAllBytesAsync(path, bytes);
        var corrupt = await Assert.ThrowsAsync<SecretVaultException>(async () => await fixture.Vault.ReadAsync("corrupt/key"));
        Assert.Equal("SECRET_VAULT_UNAVAILABLE", corrupt.ErrorCode);

        await fixture.Vault.RemoveAsync("corrupt/key");
        Assert.Null(await fixture.Vault.ReadAsync("corrupt/key"));

        using var secondVault = new FileSecretVault(
            fixture.VaultDirectory,
            DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(fixture.Root, "keys")),
                builder => builder.SetApplicationName("FLASH ROUTER BE-02 TEST")));
        var writes = Enumerable.Range(0, 30).Select(async index =>
        {
            var vault = index % 2 == 0 ? fixture.Vault : secondVault;
            await vault.StoreAsync("concurrent/key", Encoding.UTF8.GetBytes($"value-{index}"));
        });
        await Task.WhenAll(writes);
        var result = await fixture.Vault.ReadAsync("concurrent/key");
        Assert.NotNull(result);
        Assert.Matches("^value-[0-9]+$", Encoding.UTF8.GetString(result!.Value.Span));
    }

    [Fact]
    public async Task FileSecretVaultRejectsReparsePointVaultDirectoryWhenSupportedByTheHost()
    {
        using var fixture = VaultFixture.Create();
        var target = Path.Combine(fixture.Root, "real-vault");
        var link = Path.Combine(fixture.Root, "linked-vault");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        using var linkedVault = new FileSecretVault(link, DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(fixture.Root, "linked-keys")),
            builder => builder.SetApplicationName("FLASH ROUTER BE-02 LINK TEST")));
        Assert.False(await linkedVault.IsAvailableAsync());
        await Assert.ThrowsAsync<SecretVaultException>(async () =>
            await linkedVault.StoreAsync("key", Encoding.UTF8.GetBytes("value")));
    }

    [Fact]
    public async Task DpapiAuthenticatorLocksPersistsMasksAndRestoresToptCredentials()
    {
        using var fixture = VaultFixture.Create();
        var accountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var auth = new DpapiAuthenticatorUiPort(fixture.Vault, new Rfc6238TotpGenerator());

        Assert.True(await auth.IsVaultLockedAsync());
        Assert.False((await auth.UnlockVaultAsync("short")).IsSuccess);
        Assert.True((await auth.UnlockVaultAsync("synthetic-passphrase-2026")).IsSuccess);

        var add = await auth.AddCredentialAsync(
            accountId,
            "Synthetic Provider",
            "Synthetic account",
            "JBSWY3DPEHPK3PXP");
        Assert.True(add.IsSuccess);

        var credentials = await auth.GetCredentialsAsync(accountId);
        var credential = Assert.Single(credentials);
        Assert.False(credential.IsDemo);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", credential.MaskedSecret, StringComparison.Ordinal);
        Assert.NotNull(await auth.GetCurrentCodeAsync(credential.CredentialId));

        var secondAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Assert.True((await auth.AddCredentialAsync(
            secondAccountId,
            "Synthetic Provider",
            "Other account",
            "JBSWY3DPEHPK3PXP")).IsSuccess);
        Assert.Single(await auth.GetCredentialsAsync(secondAccountId));
        Assert.Single(await auth.GetCredentialsAsync(accountId));

        await auth.LockVaultAsync();
        Assert.True(await auth.IsVaultLockedAsync());
        Assert.Empty(await auth.GetCredentialsAsync(accountId));

        var restoredAuth = new DpapiAuthenticatorUiPort(fixture.Vault, new Rfc6238TotpGenerator());
        Assert.False((await restoredAuth.UnlockVaultAsync("wrong-passphrase")).IsSuccess);
        Assert.True((await restoredAuth.UnlockVaultAsync("synthetic-passphrase-2026")).IsSuccess);
        Assert.Single(await restoredAuth.GetCredentialsAsync(accountId));

        Assert.True((await restoredAuth.RemoveCredentialAsync(credential.CredentialId)).IsSuccess);
        Assert.Empty(await restoredAuth.GetCredentialsAsync(accountId));
    }

    [Fact]
    public async Task AuthenticatorDisposalLocksTheSessionAndPreventsFurtherWrites()
    {
        using var fixture = VaultFixture.Create();
        var auth = new DpapiAuthenticatorUiPort(fixture.Vault, new Rfc6238TotpGenerator());
        Assert.True((await auth.UnlockVaultAsync("synthetic-passphrase-2026")).IsSuccess);
        auth.Dispose();
        Assert.True(await auth.IsVaultLockedAsync());
        var result = await auth.AddCredentialAsync(Guid.NewGuid(), "issuer", "label", "JBSWY3DPEHPK3PXP");
        Assert.Equal("AUTHENTICATOR_DISPOSED", result.ErrorCode);
    }

    [Fact]
    public async Task SecureImportMasksLegacyRowsRejectsAmbiguousSeparatorsAndStoresOnlyEncryptedValues()
    {
        var session = SecureCredentialImportParser.CreateSession(
            "synthetic@example.test|synthetic-password|JBSWY3DPEHPK3PXP\n" +
            "second@example.test|another-password|");
        Assert.Equal(2, session.Preview.ValidRows);
        Assert.DoesNotContain("synthetic-password", session.Preview.Rows[0].MaskedPassword, StringComparison.Ordinal);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", session.Preview.Rows[0].MaskedTotp, StringComparison.Ordinal);

        using var fixture = VaultFixture.Create();
        using (session)
        {
            var result = await session.CommitAsync(
                fixture.Vault,
                new Dictionary<int, Guid>
                {
                    [1] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    [2] = Guid.Parse("44444444-4444-4444-4444-444444444444")
                },
                "synthetic-provider");
            Assert.True(result.IsSuccess);
        }

        var ciphertexts = Directory.GetFiles(fixture.VaultDirectory, "*.secret", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllBytes)
            .ToArray();
        Assert.Equal(3, ciphertexts.Length);
        foreach (var ciphertext in ciphertexts)
        {
            var encoded = Convert.ToBase64String(ciphertext);
            Assert.DoesNotContain("synthetic-password", encoded, StringComparison.Ordinal);
            Assert.DoesNotContain("another-password", encoded, StringComparison.Ordinal);
        }

        using var invalid = SecureCredentialImportParser.CreateSession(
            "bad@example.test|password|contains|separator");
        Assert.Equal(1, invalid.Preview.ErrorRows);
        Assert.False((await invalid.CommitAsync(
            fixture.Vault,
            new Dictionary<int, Guid> { [1] = Guid.NewGuid() },
            "synthetic-provider")).IsSuccess);

        using var duplicate = SecureCredentialImportParser.CreateSession(
            "duplicate@example.test|one-password|\n" +
            "duplicate@example.test|two-password|");
        Assert.Equal(1, duplicate.Preview.ErrorRows);
        Assert.Contains("duplicated", duplicate.Preview.Rows[1].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecureImportCanRecoverFromFailedConfirmationBeforeExplicitDisposal()
    {
        using var fixture = VaultFixture.Create();
        using var session = SecureCredentialImportParser.CreateSession(
            "recover@example.test|synthetic-password|JBSWY3DPEHPK3PXP");

        var failed = await session.CommitAsync(
            fixture.Vault,
            new Dictionary<int, Guid>(),
            "synthetic-provider");
        Assert.Equal("ACCOUNT_MAPPING_REQUIRED", failed.ErrorCode);
        Assert.True(!Directory.Exists(fixture.VaultDirectory)
            || Directory.GetFiles(fixture.VaultDirectory, "*.secret").Length == 0);

        var recovered = await session.CommitAsync(
            fixture.Vault,
            new Dictionary<int, Guid> { [1] = Guid.NewGuid() },
            "synthetic-provider");
        Assert.True(recovered.IsSuccess);
    }

    [Fact]
    public void SensitiveDataRedactorNeverReturnsASecretValue()
    {
        var redactor = new SensitiveDataRedactor();

        Assert.Equal("[REDACTED]", redactor.Redact("totp_secret", "synthetic-secret"));
        Assert.Equal("[REDACTED]", redactor.Redact("password", "synthetic-password"));
        Assert.Equal("safe metadata", redactor.Redact("provider", "safe metadata"));
    }

    private sealed class VaultFixture : IDisposable
    {
        private VaultFixture(string root, string vaultDirectory, FileSecretVault vault)
        {
            Root = root;
            VaultDirectory = vaultDirectory;
            Vault = vault;
        }

        public string Root { get; }
        public string VaultDirectory { get; }
        public FileSecretVault Vault { get; }

        public static string HashKey(string key)
        {
            var digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
        public static VaultFixture Create()
        {
            var root = Path.Combine(WorkspaceRoot(), ".local", "test-data", "be02-vault-" + Guid.NewGuid().ToString("N"));
            var keyRing = Path.Combine(root, "keys");
            var vaultDirectory = Path.Combine(root, "vault");
            var provider = DataProtectionProvider.Create(
                new DirectoryInfo(keyRing),
                builder => builder.SetApplicationName("FLASH ROUTER BE-02 TEST"));
            var vault = new FileSecretVault(vaultDirectory, provider);
            return new VaultFixture(root, vaultDirectory, vault);
        }

        public void Dispose()
        {
            Vault.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
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
