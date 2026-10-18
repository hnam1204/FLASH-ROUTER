using System.Data.Common;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Application;

public sealed class AccountCopyUiPort : IAccountCopyUiPort
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly BrowserAuthCredentialReader _credentialReader;
    private readonly IAuditSink? _auditSink;

    public AccountCopyUiPort(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        ISecretVault vault,
        IVaultAccessState? legacyVaultAccess = null,
        IAuditSink? auditSink = null)
    {
        _contextFactory = contextFactory;
        _credentialReader = new BrowserAuthCredentialReader(contextFactory, vault, legacyVaultAccess);
        _auditSink = auditSink;
    }

    public async Task<AccountCopyResult> CopyBasicAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await ReadAccountAsync(accountId, cancellationToken);
        return account is null
            ? AccountCopyResult.Failure("ACCOUNT_NOT_FOUND", "Tài khoản không tồn tại hoặc đã được lưu trữ.")
            : AccountCopyResult.Success(
                "BasicCopyReady",
                "Đã chuẩn bị metadata tài khoản để sao chép.",
                string.Join('|', account.Value.Email, account.Value.DisplayName, account.Value.Provider),
                containsSecret: false);
    }

    public async Task<AccountCopyResult> CopySavedCredentialsAsync(
        Guid accountId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            await AuditAsync(accountId, "CredentialExportRequested", "Credential export confirmation was not granted.", "Info", "outcome=confirmation-required", cancellationToken);
            return AccountCopyResult.Failure("COPY_CONFIRMATION_REQUIRED", "Cần xác nhận riêng trước khi sao chép email, password và 2FA vào clipboard.");
        }

        await AuditAsync(accountId, "CredentialExportRequested", "Credential export was explicitly requested by the operator.", "Warning", "outcome=requested", cancellationToken);
        var credential = await _credentialReader.ReadAsync(accountId, cancellationToken);
        if (!credential.IsSuccess || credential.Credential is null)
        {
            await AuditAsync(accountId, "CredentialExportFailed", "Credential export could not resolve a compatible protected credential.", "Warning", $"errorCode={credential.Status}", cancellationToken);
            return AccountCopyResult.Failure(credential.Status, credential.Message);
        }

        var text = CredentialLineCodec.Serialize(new CredentialLine(
            credential.Credential.Email,
            credential.Credential.Password,
            credential.Credential.TotpSeed));

        await AuditAsync(accountId, "CredentialReferenceResolved", "Protected credential reference resolved for the user-initiated clipboard operation.", "Info", "outcome=ready;payload=redacted", cancellationToken);
        return AccountCopyResult.Success(
            "CredentialCopyReady",
            "Đã chuẩn bị credential để sao chép. Clipboard có thể lưu dữ liệu plaintext.",
            text,
            containsSecret: true);
    }

    public Task RecordClipboardOutcomeAsync(
        Guid accountId,
        bool succeeded,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
        => AuditAsync(
            accountId,
            succeeded ? "AccountCredentialExportedToClipboard" : "CredentialExportFailed",
            succeeded
                ? "Credential tuple was handed to the user-initiated clipboard operation."
                : "Clipboard operation failed after protected credential resolution.",
            succeeded ? "Warning" : "Error",
            succeeded
                ? "outcome=success;payload=redacted"
                : $"outcome=clipboard-failed;errorCode={SanitizeErrorCode(errorCode)}",
            cancellationToken);

    private async Task<(string Email, string DisplayName, string Provider)?> ReadAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Email, DisplayName, Provider FROM AccountProfiles WHERE Id = $accountId AND IsArchived = 0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$accountId";
        parameter.Value = accountId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return !await reader.ReadAsync(cancellationToken)
            ? null
            : (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private async Task AuditAsync(
        Guid accountId,
        string eventType,
        string summary,
        string severity,
        string details,
        CancellationToken cancellationToken)
    {
        if (_auditSink is null)
        {
            return;
        }

        try
        {
            await _auditSink.WriteAsync(accountId, eventType, summary, severity, details, cancellationToken: cancellationToken);
        }
        catch
        {
            // Clipboard export must not expose a secret or fail because audit storage is unavailable.
        }
    }

    private static string SanitizeErrorCode(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return "CLIPBOARD_WRITE_FAILED";
        }

        return new string(errorCode
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .Take(120)
            .ToArray());
    }
}

public sealed class PreviewAccountCopyUiPort : IAccountCopyUiPort
{
    public Task<AccountCopyResult> CopyBasicAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(AccountCopyResult.Failure("PREVIEW_COPY_DISABLED", "Preview mode không chuẩn bị clipboard payload từ dữ liệu tổng hợp."));

    public Task<AccountCopyResult> CopySavedCredentialsAsync(Guid accountId, bool confirmed, CancellationToken cancellationToken = default)
        => Task.FromResult(AccountCopyResult.Failure("PREVIEW_COPY_DISABLED", "Preview mode không đọc credential. Chuyển sang Integrated mode để dùng Windows-bound credential access."));

    public Task RecordClipboardOutcomeAsync(Guid accountId, bool succeeded, string? errorCode = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
