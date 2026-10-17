using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Connections.Application;

public sealed class AuthorizationBoundary
{
    public Result<string> StartOfficialAuthorization()
        => Result<string>.Failure(
            "OAUTH_ELIGIBILITY_BLOCKED",
            "Official OAuth is implemented offline but live eligibility and registration are not confirmed.");
}
