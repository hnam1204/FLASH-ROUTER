using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Domain;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;

public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IAiProviderAdapter> _adapters;
    private readonly IReadOnlyList<ProviderDescriptor> _descriptors;

    public ProviderRegistry(IEnumerable<IAiProviderAdapter> adapters)
    {
        _adapters = adapters
            .GroupBy(adapter => adapter.Descriptor.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        _descriptors = _adapters.Values
            .Select(adapter => adapter.Descriptor)
            .Concat(ResearchOnlyProviders)
            .GroupBy(descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<ProviderDescriptor> Descriptors
        => _descriptors;

    public IAiProviderAdapter Resolve(string providerId)
        => TryResolve(providerId, out var adapter)
            ? adapter!
            : throw new ProviderConfigurationException("PROVIDER_NOT_REGISTERED", "The selected provider is not registered.");

    public bool TryResolve(string providerId, out IAiProviderAdapter? adapter)
        => _adapters.TryGetValue(providerId, out adapter);

    private static IReadOnlyList<ProviderDescriptor> ResearchOnlyProviders =>
    [
        Research("github-copilot", "GitHub Copilot", "https://github.com/features/copilot"),
        Research("cursor", "Cursor", "https://www.cursor.com/"),
        Research("windsurf", "Windsurf", "https://windsurf.com/"),
        Research("kiro", "Kiro", "https://kiro.dev/"),
        Research("antigravity", "Antigravity", "https://antigravity.google/"),
        Research("codex-cli", "Codex CLI", "https://github.com/openai/codex")
    ];

    private static ProviderDescriptor Research(string providerId, string displayName, string documentationReference)
        => new(
            providerId,
            displayName,
            ProviderType.AiWeb,
            ConnectionKind.Manual,
            ProviderCredentialKind.ManualWebCredential,
            new HashSet<ProviderCapability>(),
            documentationReference,
            "Research metadata only; no official connection flow is enabled.",
            new HashSet<string>(StringComparer.Ordinal),
            ProviderImplementationState.ResearchOnly,
            OfflineTested: false,
            LiveVerified: false,
            IsConnectable: false);
}

public sealed class ProviderConfigurationException : Exception
{
    public ProviderConfigurationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class DefaultProviderCapabilityResolver : IProviderCapabilityResolver
{
    public IReadOnlySet<ProviderCapability> Resolve(
        ProviderConnectionContext connection,
        ProviderDescriptor descriptor)
    {
        var capabilities = descriptor.SupportedCapabilities.ToHashSet();

        if (connection.ProviderId.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            // OpenRouter exposes key usage through /api/v1/key; management-level
            // credits remain permission-gated by /api/v1/credits.
            if (connection.CredentialKind is not ProviderCredentialKind.AdminApiKey
                and not ProviderCredentialKind.ProjectApiKey)
            {
                capabilities.Remove(ProviderCapability.Credits);
                capabilities.Remove(ProviderCapability.Balance);
            }

            return capabilities;
        }

        if (connection.CredentialKind is not ProviderCredentialKind.AdminApiKey
            and not ProviderCredentialKind.ProjectApiKey)
        {
            capabilities.Remove(ProviderCapability.Costs);
            capabilities.Remove(ProviderCapability.Usage);
            capabilities.Remove(ProviderCapability.TokenUsage);
        }

        if (connection.ProviderId.Equals("gemini", StringComparison.OrdinalIgnoreCase))
        {
            capabilities.Remove(ProviderCapability.Quota);
            capabilities.Remove(ProviderCapability.Costs);
            capabilities.Remove(ProviderCapability.Usage);
        }

        return capabilities;
    }
}
