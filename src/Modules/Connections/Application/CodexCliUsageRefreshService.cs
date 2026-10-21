using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

/// <summary>
/// Fails closed while Codex quota access is available only through an undocumented endpoint.
/// It intentionally does not read local auth material, call the endpoint, or bind the device's
/// current Codex session to an arbitrary selected account.
/// </summary>
public sealed class CodexCliUsageRefreshService : ICodexCliUsageRefreshPort
{
    public CodexCliUsageRefreshService() { }

    public Task<AccountUsageRefreshResult> RefreshAsync(
        Guid accountId,
        string verifiedAccountIdentity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Failure(
            accountId,
            "Nguồn quota Codex tự động chưa được xác minh hỗ trợ. Mở Codex CLI và nhập `/status` để xem hạn mức; không gán phiên đang đăng nhập trên máy cho tài khoản đang chọn.",
            "CODEX_CLI_SOURCE_UNSUPPORTED"));
    }

    private static AccountUsageRefreshResult Failure(Guid accountId, string message, string code)
        => new(accountId, code, message, 0, 1, FailedConnections: 1);

}
