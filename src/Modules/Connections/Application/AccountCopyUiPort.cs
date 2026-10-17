using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Application;

public sealed class AccountCopyUiPort : IAccountCopyUiPort
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly ISecretVault _vault;
    private readonly IVaultAccessState _vaultAccess;

    public AccountCopyUiPort(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        ISecretVault vault,
        IVaultAccessState vaultAccess)
    {
        _contextFactory = contextFactory;
        _vault = vault;
        _vaultAccess = vaultAccess;
    }

    public async Task<AccountCopyResult> CopyBasicAsync(Guid accountId, CancellationToken cancellationToken = default)
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
            return AccountCopyResult.Failure("COPY_CONFIRMATION_REQUIRED", "Cần xác nhận riêng trước khi sao chép credential plaintext.");
        }

        if (!_vaultAccess.IsUnlocked)
        {
            return AccountCopyResult.Failure("VAULT_LOCKED", "Hãy mở khóa Vault trước khi sao chép credential đã lưu.");
        }

        var reference = await ReadCredentialReferenceAsync(accountId, cancellationToken);
        if (reference is null)
        {
            return AccountCopyResult.Failure("CREDENTIAL_NOT_FOUND", "Tài khoản này chưa có credential ChatGPT đã lưu.");
        }

        var payload = await _vault.ReadAsync(reference.Value.VaultKey, cancellationToken);
        if (payload is null)
        {
            return AccountCopyResult.Failure("CREDENTIAL_UNAVAILABLE", "Không đọc được credential từ Vault. Không có dữ liệu nào được sao chép.");
        }

        var bytes = payload.Value.ToArray();
        try
        {
            var credential = JsonSerializer.Deserialize<StoredChatGptCredential>(bytes);
            if (credential is null || string.IsNullOrWhiteSpace(credential.Email) || string.IsNullOrEmpty(credential.Password))
            {
                return AccountCopyResult.Failure("CREDENTIAL_INVALID", "Credential trong Vault không hợp lệ.");
            }

            var text = CredentialLineCodec.Serialize(new CredentialLine(credential.Email, credential.Password, credential.TotpSeed));
            return AccountCopyResult.Success(
                "CredentialCopyReady",
                "Đã chuẩn bị credential để sao chép. Clipboard có thể lưu dữ liệu plaintext.",
                text,
                containsSecret: true);
        }
        catch (JsonException)
        {
            return AccountCopyResult.Failure("CREDENTIAL_INVALID", "Credential trong Vault không hợp lệ.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private async Task<(string Email, string DisplayName, string Provider)?> ReadAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await OpenConnectionAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Email, DisplayName, Provider FROM AccountProfiles WHERE Id = $accountId AND IsArchived = 0";
        AddAccountParameter(command, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return !await reader.ReadAsync(cancellationToken)
            ? null
            : (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private async Task<(string VaultKey, Guid ConnectionId)?> ReadCredentialReferenceAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await OpenConnectionAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT credentialReferences.VaultKey, connections.Id
            FROM ProviderCredentialReferences AS credentialReferences
            INNER JOIN ProviderConnections AS connections ON connections.Id = credentialReferences.ConnectionId
            WHERE connections.AccountId = $accountId
              AND connections.ProviderId = 'openai-chatgpt'
              AND connections.ConnectionKind = 'Manual'
              AND connections.Status <> 'Disconnected'
            ORDER BY connections.CreatedAtUtc DESC
            LIMIT 1
            """;
        AddAccountParameter(command, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetString(0), Guid.Parse(reader.GetString(1)));
    }

    private static async Task<DbConnection> OpenConnectionAsync(FlashAiDbContext context, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return connection;
    }

    private static void AddAccountParameter(DbCommand command, Guid accountId)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$accountId";
        parameter.Value = accountId;
        command.Parameters.Add(parameter);
    }
}

public sealed class PreviewAccountCopyUiPort : IAccountCopyUiPort
{
    public Task<AccountCopyResult> CopyBasicAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(AccountCopyResult.Failure("PREVIEW_COPY_DISABLED", "Preview mode không chuẩn bị clipboard payload từ dữ liệu tổng hợp."));

    public Task<AccountCopyResult> CopySavedCredentialsAsync(Guid accountId, bool confirmed, CancellationToken cancellationToken = default)
        => Task.FromResult(AccountCopyResult.Failure("PREVIEW_COPY_DISABLED", "Preview mode không đọc credential. Chuyển sang Integrated mode và mở khóa Vault trước."));
}
