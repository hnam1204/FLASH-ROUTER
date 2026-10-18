using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

internal sealed record CredentialProtectionFailure(string Status, string Message)
{
    public static CredentialProtectionFailure From(SecretVaultException exception)
        => exception.ErrorCode switch
        {
            "SECRET_VAULT_UNAVAILABLE" => new(
                "PROTECTION_KEY_UNAVAILABLE",
                "Không thể giải mã credential dưới Windows user context hiện tại."),
            "SECRET_VAULT_ACCESS_FAILED" => new(
                "UNAUTHORIZED_ACCESS",
                "Windows user hiện tại không có quyền đọc credential được bảo vệ."),
            "SECRET_VAULT_CORRUPT" => new(
                "CREDENTIAL_CORRUPTED",
                "Payload credential được bảo vệ không còn hợp lệ."),
            "SECRET_VAULT_PATH_INVALID" => new(
                "VAULT_STORAGE_UNAVAILABLE",
                "Đường dẫn lưu trữ credential không hợp lệ hoặc không an toàn."),
            "SECRET_VAULT_IO_FAILED" => new(
                "VAULT_STORAGE_UNAVAILABLE",
                "Không thể truy cập vùng lưu trữ credential hiện tại."),
            _ => new(
                "CREDENTIAL_READ_FAILURE",
                "Không thể đọc credential được bảo vệ trong phiên hiện tại.")
        };

    public static BrowserAuthCredentialReadinessKind ReadinessKind(string status)
        => status switch
        {
            "PROTECTION_KEY_UNAVAILABLE" => BrowserAuthCredentialReadinessKind.ProtectionKeyUnavailable,
            "UNAUTHORIZED_ACCESS" => BrowserAuthCredentialReadinessKind.UnauthorizedAccess,
            "CREDENTIAL_CORRUPTED" => BrowserAuthCredentialReadinessKind.CredentialCorrupted,
            "VAULT_STORAGE_UNAVAILABLE" => BrowserAuthCredentialReadinessKind.VaultStorageUnavailable,
            _ => BrowserAuthCredentialReadinessKind.CredentialReadFailure
        };
}
