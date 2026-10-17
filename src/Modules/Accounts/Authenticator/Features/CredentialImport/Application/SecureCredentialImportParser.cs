using System.Security.Cryptography;
using System.Text.Json;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Authenticator.Application;

namespace FlashAI.Modules.Authenticator.Features.CredentialImport.Application;

public sealed class SecureCredentialImportParser
{
    public static SecureCredentialImportSession CreateSession(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = new List<PendingRow>();
        var previewRows = new List<CredentialImportPreviewRow>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var rowNumber = index + 1;
            var parts = line.Split('|');
            var email = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            var password = parts.Length > 1 ? parts[1] : string.Empty;
            var totp = parts.Length > 2 ? parts[2].Trim() : string.Empty;
            var errors = new List<string>();

            if (parts.Length != 3)
            {
                errors.Add("Each row must contain exactly email|password|totp; passwords containing | are rejected.");
            }

            if (!IsEmail(email))
            {
                errors.Add("The email address is invalid.");
            }

            if (password.Length == 0 || password.Length > 512)
            {
                errors.Add("The password is required and must be 512 characters or fewer.");
            }

            if (!string.IsNullOrWhiteSpace(totp))
            {
                try
                {
                    var decoded = Base32SecretDecoder.Decode(totp);
                    CryptographicOperations.ZeroMemory(decoded);
                }
                catch (FormatException)
                {
                    errors.Add("The TOTP value is not valid Base32.");
                }
            }

            if (!seenEmails.Add(email))
            {
                errors.Add("The email is duplicated in this import.");
            }

            var isValid = errors.Count == 0;
            previewRows.Add(new CredentialImportPreviewRow(
                rowNumber,
                email,
                Mask(password),
                Mask(totp),
                isValid,
                isValid ? null : string.Join(" ", errors)));

            rows.Add(isValid
                ? new PendingRow(rowNumber, email, password.ToCharArray(), totp.ToCharArray())
                : PendingRow.Invalid(rowNumber));
        }

        return new SecureCredentialImportSession(
            new CredentialImportPreview(
                previewRows.Count,
                previewRows.Count(row => row.IsValid),
                previewRows.Count(row => !row.IsValid),
                previewRows),
            rows);
    }

    private static bool IsEmail(string email)
        => email.Length <= 320
            && !email.Any(char.IsWhiteSpace)
            && email.Count(character => character == '@') == 1
            && email[(email.IndexOf('@') + 1)..].Contains('.');

    private static string Mask(string value)
        => string.IsNullOrEmpty(value) ? "(empty)" : "********";
}

public sealed record CredentialImportPreviewRow(
    int RowNumber,
    string Email,
    string MaskedPassword,
    string MaskedTotp,
    bool IsValid,
    string? Error);

public sealed record CredentialImportPreview(
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    IReadOnlyList<CredentialImportPreviewRow> Rows);

internal sealed class PendingRow
{
    internal PendingRow(int rowNumber, string email, char[] password, char[] totp)
    {
        RowNumber = rowNumber;
        Email = email;
        Password = password;
        Totp = totp;
    }

    public int RowNumber { get; }
    public string Email { get; }
    public char[] Password { get; private set; }
    public char[] Totp { get; private set; }

    public static PendingRow Invalid(int rowNumber) => new(rowNumber, string.Empty, [], []);

    public void Clear()
    {
        Array.Clear(Password);
        Array.Clear(Totp);
        Password = [];
        Totp = [];
    }
}

public sealed class SecureCredentialImportSession : IDisposable
{
    private readonly List<PendingRow> _rows;
    private bool _committed;
    private bool _disposed;

    internal SecureCredentialImportSession(CredentialImportPreview preview, List<PendingRow> rows)
    {
        Preview = preview;
        _rows = rows;
    }

    public CredentialImportPreview Preview { get; }

    public async Task<OperationResultVm> CommitAsync(
        ISecretVault vault,
        IReadOnlyDictionary<int, Guid> accountIds,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vault);

        if (_disposed)
        {
            return OperationResultVm.Failure("IMPORT_SESSION_DISPOSED", "This import session has been disposed.");
        }

        if (_committed)
        {
            return OperationResultVm.Failure("IMPORT_ALREADY_COMMITTED", "This import session has already been committed.");
        }

        if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 128)
        {
            return OperationResultVm.Failure("INVALID_PROVIDER", "A provider is required for credential import.");
        }

        if (Preview.ErrorRows > 0)
        {
            return OperationResultVm.Failure("INVALID_IMPORT", "Resolve all invalid import rows before confirmation.");
        }

        var createdKeys = new List<string>();
        var storedCount = 0;
        try
        {
            foreach (var row in _rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!accountIds.TryGetValue(row.RowNumber, out var accountId) || accountId == Guid.Empty)
                {
                    return OperationResultVm.Failure("ACCOUNT_MAPPING_REQUIRED", "Each import row must be mapped to an account before confirmation.");
                }

                if (row.Password.Length > 0)
                {
                    var credentialId = Guid.NewGuid();
                    await StoreCredentialAsync(
                        vault,
                        credentialId,
                        accountId,
                        providerId,
                        row.Email,
                        "Password",
                        new string(row.Password),
                        cancellationToken);
                    createdKeys.Add(GetCredentialKey(credentialId));
                    storedCount++;
                }

                if (row.Totp.Length > 0)
                {
                    var credentialId = Guid.NewGuid();
                    await StoreCredentialAsync(
                        vault,
                        credentialId,
                        accountId,
                        providerId,
                        row.Email,
                        "Totp",
                        new string(row.Totp),
                        cancellationToken);
                    createdKeys.Add(GetCredentialKey(credentialId));
                    storedCount++;
                }
            }

            _committed = true;
            return OperationResultVm.Success($"Stored {storedCount} credential value(s) in the encrypted local vault.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SecretVaultException exception)
        {
            return OperationResultVm.Failure(exception.ErrorCode, exception.Message);
        }
        finally
        {
            if (!_committed)
            {
                foreach (var key in createdKeys)
                {
                    try
                    {
                        await vault.RemoveAsync(key, CancellationToken.None);
                    }
                    catch (SecretVaultException)
                    {
                        // Keep the original safe failure; an orphaned ciphertext is still inaccessible without the vault key.
                    }
                }
            }

            if (_committed)
            {
                ClearRows();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearRows();
    }

    private static async Task StoreCredentialAsync(
        ISecretVault vault,
        Guid credentialId,
        Guid accountId,
        string providerId,
        string email,
        string kind,
        string value,
        CancellationToken cancellationToken)
    {
        var envelope = new ImportedCredentialEnvelope(
            credentialId,
            accountId,
            providerId,
            email,
            kind,
            DateTimeOffset.UtcNow,
            value);
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        try
        {
            await vault.StoreAsync(GetCredentialKey(credentialId), payload, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static string GetCredentialKey(Guid credentialId) => $"credential-import/{credentialId:N}";

    private void ClearRows()
    {
        foreach (var row in _rows)
        {
            row.Clear();
        }
    }

    private sealed record ImportedCredentialEnvelope(
        Guid CredentialId,
        Guid AccountId,
        string ProviderId,
        string Email,
        string Kind,
        DateTimeOffset CreatedAtUtc,
        string Value);
}
