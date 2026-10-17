using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Application;

public sealed record StoredChatGptCredentialReadResult(
    bool IsSuccess,
    string Status,
    string Message,
    string? AccountEmail = null,
    StoredChatGptCredential? Credential = null,
    Guid? ConnectionId = null,
    Guid? CredentialReferenceId = null)
{
    public static StoredChatGptCredentialReadResult Failure(
        string status,
        string message,
        string? accountEmail = null,
        Guid? connectionId = null,
        Guid? credentialReferenceId = null)
        => new(false, status, message, accountEmail, null, connectionId, credentialReferenceId);
}

public sealed record BrowserAuthCredentialReadiness(
    bool IsReady,
    string Status,
    string Message,
    string? AccountEmail = null,
    BrowserAuthCredentialReadinessKind Kind = BrowserAuthCredentialReadinessKind.CredentialReferenceMissing,
    Guid? ConnectionId = null,
    Guid? CredentialReferenceId = null);

public sealed record BrowserAuthCredentialReferenceSnapshot(
    Guid? ConnectionId,
    Guid? CredentialReferenceId);

public sealed class BrowserAuthCredentialReader
{
    private const string ProviderId = "openai-chatgpt";
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly ISecretVault _vault;
    private readonly IVaultAccessState _vaultAccess;

    public BrowserAuthCredentialReader(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        ISecretVault vault,
        IVaultAccessState vaultAccess)
    {
        _contextFactory = contextFactory;
        _vault = vault;
        _vaultAccess = vaultAccess;
    }

    public async Task<string?> GetAccountEmailAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await OpenConnectionAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Email FROM AccountProfiles WHERE Id = $accountId AND IsArchived = 0";
        AddAccountParameter(command, accountId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    public async Task<BrowserAuthCredentialReadiness> CheckReadinessAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var accountEmail = await GetAccountEmailAsync(accountId, cancellationToken);
        if (string.IsNullOrWhiteSpace(accountEmail))
        {
            return new BrowserAuthCredentialReadiness(
                false,
                "ACCOUNT_NOT_FOUND",
                "Tài khoản không tồn tại hoặc đã được lưu trữ.",
                Kind: BrowserAuthCredentialReadinessKind.CredentialReferenceMissing);
        }

        var reference = await FindReferenceAsync(accountId, ProviderId, cancellationToken);
        if (!_vaultAccess.IsUnlocked)
        {
            return new BrowserAuthCredentialReadiness(
                false,
                "VAULT_LOCKED",
                "Hãy mở khóa Vault trước khi cho phép Auto Login.",
                accountEmail,
                BrowserAuthCredentialReadinessKind.VaultLocked,
                reference?.ConnectionId,
                reference?.CredentialReferenceId);
        }

        if (reference is null)
        {
            var otherProviderReference = await FindAnyReferenceAsync(accountId, cancellationToken);
            return otherProviderReference is not null
                ? new BrowserAuthCredentialReadiness(
                    false,
                    "CREDENTIAL_PROVIDER_MISMATCH",
                    "Credential đã lưu không thuộc provider ChatGPT được chọn.",
                    accountEmail,
                    BrowserAuthCredentialReadinessKind.CredentialProviderMismatch,
                    otherProviderReference.ConnectionId,
                    otherProviderReference.CredentialReferenceId)
                : new BrowserAuthCredentialReadiness(
                    false,
                    "CREDENTIAL_NOT_FOUND",
                    "Tài khoản này chưa có credential ChatGPT đã lưu trong Vault.",
                    accountEmail,
                    BrowserAuthCredentialReadinessKind.CredentialReferenceMissing);
        }

        try
        {
            var payload = await _vault.ReadAsync(reference.VaultKey, cancellationToken);
            if (payload is null)
            {
                return new BrowserAuthCredentialReadiness(
                    false,
                    "CREDENTIAL_UNAVAILABLE",
                    "Không đọc được credential từ Vault.",
                    accountEmail,
                    BrowserAuthCredentialReadinessKind.CredentialPayloadUnavailable,
                    reference.ConnectionId,
                    reference.CredentialReferenceId);
            }

            var bytes = payload.Value.ToArray();
            try
            {
                if (!TryReadCredential(bytes, accountEmail, out _, out var status))
                {
                    return new BrowserAuthCredentialReadiness(
                        false,
                        status,
                        status == "CREDENTIAL_OWNERSHIP_MISMATCH"
                            ? "Credential trong Vault không thuộc email của tài khoản đang chọn."
                            : "Credential trong Vault không có payload hợp lệ.",
                        accountEmail,
                        status == "CREDENTIAL_OWNERSHIP_MISMATCH"
                            ? BrowserAuthCredentialReadinessKind.CredentialOwnershipMismatch
                            : BrowserAuthCredentialReadinessKind.CredentialPayloadUnavailable,
                        reference.ConnectionId,
                        reference.CredentialReferenceId);
                }

                return new BrowserAuthCredentialReadiness(
                    true,
                    "CREDENTIAL_REFERENCE_READY",
                    "Credential reference và payload của tài khoản đã sẵn sàng; secret chỉ được dùng sau khi cấp quyền.",
                    accountEmail,
                    BrowserAuthCredentialReadinessKind.CredentialReady,
                    reference.ConnectionId,
                    reference.CredentialReferenceId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (SecretVaultException)
        {
            return new BrowserAuthCredentialReadiness(
                false,
                "CREDENTIAL_READ_FAILURE",
                "Không thể đọc credential từ Vault hiện tại.",
                accountEmail,
                BrowserAuthCredentialReadinessKind.CredentialReadFailure,
                reference.ConnectionId,
                reference.CredentialReferenceId);
        }
    }

    public async Task<BrowserAuthCredentialReferenceSnapshot> GetReferenceSnapshotAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var reference = await FindReferenceAsync(accountId, ProviderId, cancellationToken);
        return reference is null
            ? new BrowserAuthCredentialReferenceSnapshot(null, null)
            : new BrowserAuthCredentialReferenceSnapshot(reference.ConnectionId, reference.CredentialReferenceId);
    }

    public async Task<StoredChatGptCredentialReadResult> ReadAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var accountEmail = await GetAccountEmailAsync(accountId, cancellationToken);
        if (string.IsNullOrWhiteSpace(accountEmail))
        {
            return StoredChatGptCredentialReadResult.Failure(
                "ACCOUNT_NOT_FOUND",
                "Tài khoản không tồn tại hoặc đã được lưu trữ.");
        }

        var reference = await FindReferenceAsync(accountId, ProviderId, cancellationToken);
        if (reference is null)
        {
            return StoredChatGptCredentialReadResult.Failure(
                "CREDENTIAL_NOT_FOUND",
                "Tài khoản này chưa có credential ChatGPT đã lưu.",
                accountEmail);
        }

        if (!_vaultAccess.IsUnlocked)
        {
            return StoredChatGptCredentialReadResult.Failure(
                "VAULT_LOCKED",
                "Hãy mở khóa Vault trước khi điền thông tin nhạy cảm.",
                accountEmail,
                reference.ConnectionId,
                reference.CredentialReferenceId);
        }

        ReadOnlyMemory<byte>? payload;
        try
        {
            payload = await _vault.ReadAsync(reference.VaultKey, cancellationToken);
        }
        catch (SecretVaultException)
        {
            return StoredChatGptCredentialReadResult.Failure(
                "CREDENTIAL_READ_FAILURE",
                "Không thể đọc credential từ Vault hiện tại.",
                accountEmail,
                reference.ConnectionId,
                reference.CredentialReferenceId);
        }

        if (payload is null)
        {
            return StoredChatGptCredentialReadResult.Failure(
                "CREDENTIAL_UNAVAILABLE",
                "Không đọc được credential từ Vault.",
                accountEmail,
                reference.ConnectionId,
                reference.CredentialReferenceId);
        }

        var bytes = payload.Value.ToArray();
        try
        {
            if (!TryReadCredential(bytes, accountEmail, out var credential, out var status))
            {
                return StoredChatGptCredentialReadResult.Failure(
                    status,
                    status == "CREDENTIAL_OWNERSHIP_MISMATCH"
                        ? "Credential trong Vault không thuộc email của tài khoản đang chọn."
                        : "Credential trong Vault không hợp lệ.",
                    accountEmail,
                    reference.ConnectionId,
                    reference.CredentialReferenceId);
            }

            return new StoredChatGptCredentialReadResult(
                true,
                "CredentialReady",
                "Credential đã được đọc từ Vault theo tài khoản đã chọn.",
                accountEmail,
                credential,
                reference.ConnectionId,
                reference.CredentialReferenceId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private async Task<CredentialReferenceMetadata?> FindReferenceAsync(
        Guid accountId,
        string providerId,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await OpenConnectionAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT connections.Id, connections.ProviderId, credentialReferences.Id, credentialReferences.VaultKey
            FROM ProviderCredentialReferences AS credentialReferences
            INNER JOIN ProviderConnections AS connections ON connections.Id = credentialReferences.ConnectionId
            WHERE connections.AccountId = $accountId
              AND connections.ProviderId = $providerId
              AND connections.ConnectionKind = 'Manual'
              AND connections.Status <> 'Disconnected'
              AND credentialReferences.ProviderId = $providerId
              AND credentialReferences.CredentialKind = 'ManualWebCredential'
            ORDER BY connections.CreatedAtUtc DESC
            LIMIT 1
            """;
        AddAccountParameter(command, accountId);
        AddParameter(command, "$providerId", providerId);
        return await ReadReferenceAsync(command, cancellationToken);
    }

    private async Task<CredentialReferenceMetadata?> FindAnyReferenceAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await OpenConnectionAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT connections.Id, connections.ProviderId, credentialReferences.Id, credentialReferences.VaultKey
            FROM ProviderCredentialReferences AS credentialReferences
            INNER JOIN ProviderConnections AS connections ON connections.Id = credentialReferences.ConnectionId
            WHERE connections.AccountId = $accountId
              AND connections.ConnectionKind = 'Manual'
              AND connections.Status <> 'Disconnected'
              AND credentialReferences.CredentialKind = 'ManualWebCredential'
            ORDER BY connections.CreatedAtUtc DESC
            LIMIT 1
            """;
        AddAccountParameter(command, accountId);
        return await ReadReferenceAsync(command, cancellationToken);
    }

    private static async Task<CredentialReferenceMetadata?> ReadReferenceAsync(
        DbCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CredentialReferenceMetadata(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3));
    }

    private static bool TryReadCredential(
        byte[] bytes,
        string accountEmail,
        out StoredChatGptCredential? credential,
        out string status)
    {
        credential = null;
        status = "CREDENTIAL_UNAVAILABLE";
        try
        {
            credential = JsonSerializer.Deserialize<StoredChatGptCredential>(bytes);
            if (credential is null
                || string.IsNullOrWhiteSpace(credential.Email)
                || string.IsNullOrEmpty(credential.Password))
            {
                return false;
            }

            if (!credential.Email.Equals(accountEmail, StringComparison.OrdinalIgnoreCase))
            {
                status = "CREDENTIAL_OWNERSHIP_MISMATCH";
                credential = null;
                return false;
            }

            status = "CredentialReady";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<DbConnection> OpenConnectionAsync(
        FlashAiDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }

    private static void AddAccountParameter(DbCommand command, Guid accountId)
        => AddParameter(command, "$accountId", accountId);

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record CredentialReferenceMetadata(
        Guid ConnectionId,
        string ProviderId,
        Guid CredentialReferenceId,
        string VaultKey);
}
