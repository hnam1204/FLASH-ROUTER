using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.RefreshConnection.Application;

namespace FlashAI.Modules.Connections.Application;

public sealed class DurableConnectionUiPort : IConnectionUiPort
{
    private readonly IOAuthConnectionStore _store;
    private readonly OAuthConnectionSessionService _sessionService;
    private readonly IOfficialOAuthEligibility _eligibility;

    public DurableConnectionUiPort(
        IOAuthConnectionStore store,
        OAuthConnectionSessionService sessionService,
        IOfficialOAuthEligibility eligibility)
    {
        _store = store;
        _sessionService = sessionService;
        _eligibility = eligibility;
    }

    public async Task<ConnectionStatusVm> GetStatusAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.GetForAccountAsync(accountId, cancellationToken);
        if (snapshot is null)
        {
            return new ConnectionStatusVm(
                accountId,
                "OpenAI ChatGPT Official",
                "AuthorizationRequired",
                "Cần cấp quyền",
                null,
                null,
                null,
                null,
                _eligibility.StatusMessage,
                true);
        }

        var scopes = snapshot.GrantedScopes.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return new ConnectionStatusVm(
            accountId,
            "OpenAI ChatGPT Official",
            snapshot.Status.ToString(),
            ToLabel(snapshot.Status),
            snapshot.ProviderSubject,
            snapshot.AuthorizedAtUtc,
            snapshot.LastVerifiedAtUtc,
            null,
            snapshot.Status is ConnectionStatus.NeedsReauthorization
                ? "Cần xác thực lại bằng OAuth chính thức."
                : "Thông tin kết nối lấy từ metadata cục bộ đã lưu.",
            true,
            scopes,
            snapshot.AccessTokenExpiresAtUtc,
            scopes.Contains(OfficialOAuthScopes.ChatGptPlanUse, StringComparer.Ordinal));
    }

    public Task<OperationResultVm> RequestConnectAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResultVm.Failure(
            "OAUTH_UNAVAILABLE",
            _eligibility.StatusMessage));

    public Task<OperationResultVm> RequestReconnectAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResultVm.Failure(
            "OAUTH_UNAVAILABLE",
            _eligibility.StatusMessage));

    public async Task<OperationResultVm> RequestDisconnectAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.GetForAccountAsync(accountId, cancellationToken);
        return snapshot is null
            ? OperationResultVm.Failure("CONNECTION_NOT_FOUND", "No local connection exists for this account.")
            : await _sessionService.DisconnectAsync(snapshot.ConnectionId, cancellationToken);
    }

    private static string ToLabel(ConnectionStatus status)
        => status switch
        {
            ConnectionStatus.Authorized => "Đã cấp quyền",
            ConnectionStatus.Pending => "Đang chờ",
            ConnectionStatus.NeedsReauthorization => "Cần xác thực lại",
            ConnectionStatus.Disconnected => "Đã ngắt kết nối",
            ConnectionStatus.Revoked => "Đã thu hồi",
            ConnectionStatus.Error => "Lỗi kết nối",
            _ => "Chưa cấu hình"
        };
}
