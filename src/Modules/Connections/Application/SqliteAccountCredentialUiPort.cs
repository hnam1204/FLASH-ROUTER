using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashAI.Foundation.Persistence;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Connections.Application;

public sealed class SqliteAccountCredentialUiPort : IAccountCredentialUiPort
{
    private const string ProviderId = "openai-chatgpt";
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;
    private readonly ISecretVault _vault;
    private readonly IVaultAccessState? _vaultAccess;

    public SqliteAccountCredentialUiPort(
        IDbContextFactory<FlashAiDbContext> contextFactory,
        ISecretVault vault,
        IVaultAccessState? vaultAccess = null)
    {
        _contextFactory = contextFactory;
        _vault = vault;
        _vaultAccess = vaultAccess;
    }

    public async Task<CredentialConnectionResult> SaveAsync(
        Guid accountId,
        ChatGptCredentialInput input,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return CredentialConnectionResult.Failure("ACCOUNT_REQUIRED", "Không xác định được tài khoản cần lưu.");
        }

        if (string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrEmpty(input.Password))
        {
            return CredentialConnectionResult.Failure("VALIDATION_FAILED", "Email và mật khẩu là bắt buộc.");
        }

        if (!string.IsNullOrWhiteSpace(input.TotpSeed) && !LooksLikeBase32Seed(input.TotpSeed))
        {
            return CredentialConnectionResult.Failure("TOTP_SEED_INVALID", "TOTP seed không có định dạng Base32 hợp lệ.");
        }

        if (!await _vault.IsAvailableAsync(cancellationToken))
        {
            return CredentialConnectionResult.Failure("VAULT_UNAVAILABLE", "Vault mã hóa chưa sẵn sàng. Không có dữ liệu plaintext nào được lưu.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var duplicate = await context.Set<ProviderConnectionRecord>().AnyAsync(connection =>
            connection.AccountId == accountId
            && connection.ProviderId == ProviderId
            && connection.ConnectionKind == ConnectionKind.Manual
            && connection.Status != ConnectionStatus.Disconnected,
            cancellationToken);
        if (duplicate)
        {
            return CredentialConnectionResult.Failure("DUPLICATE_CREDENTIAL", "Tài khoản này đã có credential ChatGPT được lưu.");
        }

        var connectionId = Guid.NewGuid();
        var credentialId = $"cred_{Guid.NewGuid():N}";
        var vaultKey = GetVaultKey(connectionId);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new StoredChatGptCredential(
            input.Email.Trim(),
            input.Password,
            string.IsNullOrWhiteSpace(input.TotpSeed) ? null : input.TotpSeed.Trim()));

        try
        {
            await _vault.StoreAsync(vaultKey, payload, cancellationToken);

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            context.Add(new ProviderConnectionRecord(
                connectionId,
                accountId,
                ProviderId,
                ConnectionKind.Manual,
                "ChatGPT - đăng nhập trình duyệt",
                null,
                ConnectionStatus.CredentialStored,
                now,
                now));
            context.Add(new ProviderCredentialReferenceRecord(
                Guid.NewGuid(),
                connectionId,
                ProviderId,
                credentialId,
                ProviderCredentialKind.ManualWebCredential,
                vaultKey,
                null,
                now,
                now));
            context.Add(new ProviderSyncStateRecord(
                Guid.NewGuid(),
                connectionId,
                ProviderId,
                false,
                null,
                null,
                0,
                now));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await RemoveSecretAfterFailureAsync(vaultKey, cancellationToken);
            throw;
        }
        catch
        {
            var cleanupSucceeded = await RemoveSecretAfterFailureAsync(vaultKey, CancellationToken.None);
            return cleanupSucceeded
                ? CredentialConnectionResult.Failure("SQLITE_COMMIT_FAILED", "Không thể lưu tham chiếu credential vào SQLite; secret đã được dọn khỏi Vault.")
                : CredentialConnectionResult.Failure("CREDENTIAL_ROLLBACK_REQUIRED", "Không thể hoàn tất lưu credential và chưa xác nhận được việc dọn secret khỏi Vault.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        return CredentialConnectionResult.Success(
            "CredentialStored",
            "Đã lưu credential trong Vault mã hóa. Hãy mở Chrome bằng profile của tài khoản để hoàn tất đăng nhập trên website chính thức; trạng thái này không đồng nghĩa đã đăng nhập.",
            connectionId);
    }

    public async Task<CredentialConnectionResult> ReplaceAsync(
        Guid accountId,
        ChatGptCredentialInput input,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return CredentialConnectionResult.Failure("ACCOUNT_REQUIRED", "Không xác định được tài khoản cần cập nhật.");
        }

        if (string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrEmpty(input.Password))
        {
            return CredentialConnectionResult.Failure("VALIDATION_FAILED", "Email và mật khẩu là bắt buộc.");
        }

        if (!string.IsNullOrWhiteSpace(input.TotpSeed) && !LooksLikeBase32Seed(input.TotpSeed))
        {
            return CredentialConnectionResult.Failure("TOTP_SEED_INVALID", "TOTP seed không có định dạng Base32 hợp lệ.");
        }

        if (_vaultAccess is not null && !_vaultAccess.IsUnlocked)
        {
            return CredentialConnectionResult.Failure("VAULT_LOCKED", "Hãy mở khóa Vault trước khi cập nhật credential.");
        }

        if (!await _vault.IsAvailableAsync(cancellationToken))
        {
            return CredentialConnectionResult.Failure("VAULT_UNAVAILABLE", "Vault mã hóa chưa sẵn sàng; credential hiện hữu chưa bị thay đổi.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await (
            from connection in context.Set<ProviderConnectionRecord>()
            join reference in context.Set<ProviderCredentialReferenceRecord>()
                on connection.Id equals reference.ConnectionId
            where connection.AccountId == accountId
                && connection.ProviderId == ProviderId
                && connection.ConnectionKind == ConnectionKind.Manual
                && connection.Status != ConnectionStatus.Disconnected
                && reference.ProviderId == ProviderId
                && reference.CredentialKind == ProviderCredentialKind.ManualWebCredential
            select new
            {
                Connection = connection,
                Reference = reference
            }).ToListAsync(cancellationToken);

        var existing = candidates
            .OrderByDescending(candidate => candidate.Connection.CreatedAtUtc)
            .FirstOrDefault();

        if (existing is null)
        {
            return CredentialConnectionResult.Failure("CREDENTIAL_NOT_FOUND", "Không có credential ChatGPT hiện hữu để cập nhật.");
        }

        var accountEmail = await GetAccountEmailAsync(context, accountId, cancellationToken);
        if (string.IsNullOrWhiteSpace(accountEmail)
            || !accountEmail.Equals(input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return CredentialConnectionResult.Failure("ACCOUNT_EMAIL_MISMATCH", "Email cập nhật phải khớp tài khoản đang chọn.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new StoredChatGptCredential(
            input.Email.Trim(),
            input.Password,
            string.IsNullOrWhiteSpace(input.TotpSeed) ? null : input.TotpSeed.Trim()));
        try
        {
            // FileSecretVault replaces the same vault entry atomically; the existing
            // connection and reference identities remain unchanged.
            await _vault.StoreAsync(existing.Reference.VaultKey, payload, cancellationToken);
            existing.Connection.MarkStatus(ConnectionStatus.CredentialStored, DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (SecretVaultException exception)
        {
            return CredentialConnectionResult.Failure(exception.ErrorCode, "Không thể cập nhật credential trong Vault mã hóa.");
        }
        catch
        {
            return CredentialConnectionResult.Failure("CREDENTIAL_UPDATE_FAILED", "Không thể hoàn tất cập nhật credential; dữ liệu liên kết hiện hữu được giữ nguyên.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        return CredentialConnectionResult.Success(
            "CredentialReplaced",
            "Đã thay thế credential của đúng tài khoản trong Vault mã hóa.",
            existing.Connection.Id);
    }

    private static async Task<string?> GetAccountEmailAsync(
        FlashAiDbContext context,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Email FROM AccountProfiles WHERE Id = $accountId AND IsArchived = 0";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$accountId";
        parameter.Value = accountId;
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public static string GetVaultKey(Guid connectionId)
        => $"connections/web/{ProviderId}/{connectionId:N}/manual";

    private static bool LooksLikeBase32Seed(string value)
    {
        var normalized = value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        return normalized.Length >= 8
            && normalized.All(character => (character >= 'A' && character <= 'Z') || (character >= '2' && character <= '7') || character == '=');
    }

    private async Task<bool> RemoveSecretAfterFailureAsync(string vaultKey, CancellationToken cancellationToken)
    {
        try
        {
            await _vault.RemoveAsync(vaultKey, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

}

public sealed class PreviewAccountCredentialUiPort : IAccountCredentialUiPort
{
    public Task<CredentialConnectionResult> SaveAsync(
        Guid accountId,
        ChatGptCredentialInput input,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CredentialConnectionResult.Failure(
            "PREVIEW_CREDENTIALS_DISABLED",
            "Preview mode không lưu credential. Chuyển sang Integrated mode để sử dụng Vault mã hóa thật."));

    public Task<CredentialConnectionResult> ReplaceAsync(
        Guid accountId,
        ChatGptCredentialInput input,
        CancellationToken cancellationToken = default)
        => Task.FromResult(CredentialConnectionResult.Failure(
            "PREVIEW_CREDENTIALS_DISABLED",
            "Preview mode không cập nhật credential. Chuyển sang Integrated mode để sử dụng Vault mã hóa thật."));
}
