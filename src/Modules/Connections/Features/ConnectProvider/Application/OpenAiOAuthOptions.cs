namespace FlashAI.Modules.Connections.Features.ConnectProvider.Application;

public sealed record OpenAiOAuthOptions(
    string AgentName,
    bool EligibilityConfirmed,
    TimeSpan AuthorizationAttemptLifetime,
    int CallbackPort = 1455)
{
    public const string ProviderId = "openai-chatgpt-official";
    public const string DynamicClientId = "dynamic_agent_client";
    public const string AuthorizationEndpoint = "https://auth.openai.com/api/accounts/authorize";
    public const string TokenEndpoint = "https://auth.openai.com/api/accounts/oauth/token";
    public const string OpenIdConfigurationEndpoint = "https://auth.openai.com/.well-known/openid-configuration";
    public const string Resource = "https://api.openai.com/v1";
    public const string Issuer = "https://auth.openai.com";
    public const string CallbackPath = "/auth/callback";

    public static OpenAiOAuthOptions BlockedDefault => new(
        "FLASH ROUTER",
        false,
        TimeSpan.FromMinutes(5));
}
