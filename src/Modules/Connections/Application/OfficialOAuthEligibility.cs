using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;

namespace FlashAI.Modules.Connections.Application;

public sealed class OfficialOAuthEligibility : IOfficialOAuthEligibility
{
    private readonly OpenAiOAuthOptions _options;

    public OfficialOAuthEligibility(OpenAiOAuthOptions options)
    {
        _options = options;
    }

    public bool IsConfirmedForThisApplication => _options.EligibilityConfirmed;

    public string StatusMessage => IsConfirmedForThisApplication
        ? "Official OAuth adapter is configured for offline validation; live authorization still requires explicit user approval."
        : "Official OAuth capability is blocked until FLASH ROUTER eligibility is confirmed for this application.";
}
