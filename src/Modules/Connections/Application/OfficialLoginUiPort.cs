using System.Diagnostics;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Integration;

namespace FlashAI.Modules.Connections.Application;

public sealed class OfficialLoginUiPort : IOfficialLoginUiPort
{
    private readonly IOfficialAuthorizationPort _authorization;
    private readonly IOfficialOAuthEligibility _eligibility;

    public OfficialLoginUiPort(
        IOfficialAuthorizationPort authorization,
        IOfficialOAuthEligibility eligibility)
    {
        _authorization = authorization;
        _eligibility = eligibility;
    }

    public async Task<OfficialLoginStartResult> StartAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (!_eligibility.IsConfirmedForThisApplication)
        {
            return OfficialLoginStartResult.Failure("OAUTH_ELIGIBILITY_BLOCKED", _eligibility.StatusMessage);
        }

        var authorization = await _authorization.StartAsync(
            accountId,
            cancellationToken: cancellationToken);
        if (!authorization.IsSuccess || authorization.Value is null)
        {
            return OfficialLoginStartResult.Failure(
                authorization.ErrorCode ?? "OAUTH_START_FAILED",
                authorization.ErrorMessage ?? "Không thể bắt đầu OAuth chính thức.");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authorization.Value.AuthorizationUri,
                UseShellExecute = true
            });
            return OfficialLoginStartResult.Success(
                "OAuthPending",
                "Đã mở luồng OAuth chính thức. Chỉ đánh dấu Authorized sau khi callback và identity được xác minh.");
        }
        catch
        {
            return OfficialLoginStartResult.Failure(
                "OAUTH_BROWSER_FAILED",
                "Không mở được trình duyệt OAuth. Hồ sơ vẫn giữ trạng thái chờ xác thực.");
        }
    }
}
