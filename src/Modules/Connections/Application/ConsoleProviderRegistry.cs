using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

public sealed class ConsoleProviderRegistry : IConsoleProviderRegistry
{
    private static readonly IReadOnlyList<ConsoleProviderOption> Providers =
    [
        new(
            "openai-chatgpt",
            "ChatGPT",
            "Official website login flow",
            true,
            BrowserOriginPolicy.ChatGptUri,
            ConsoleProviderCapability.VerifiedWebAdapter),
        new(
            "anthropic-claude",
            "Claude",
            "Web login adapter is planned",
            false,
            new Uri("https://claude.ai/", UriKind.Absolute),
            ConsoleProviderCapability.Unsupported),
        new(
            "google-gemini",
            "Gemini",
            "Web login adapter is planned",
            false,
            new Uri("https://gemini.google.com/", UriKind.Absolute),
            ConsoleProviderCapability.Unsupported),
        new(
            "openrouter",
            "OpenRouter",
            "Web login adapter is planned",
            false,
            new Uri("https://openrouter.ai/", UriKind.Absolute),
            ConsoleProviderCapability.Unsupported)
    ];

    public IReadOnlyList<ConsoleProviderOption> List() => Providers;

    public bool TryGet(string providerId, out ConsoleProviderOption? provider)
    {
        provider = Providers.FirstOrDefault(item =>
            item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        return provider is not null;
    }
}
