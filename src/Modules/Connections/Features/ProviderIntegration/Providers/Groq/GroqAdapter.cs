using System.Globalization;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Groq;

public sealed class GroqAdapter : ProviderAdapterBase
{
    private readonly ProviderHttpExecutor _http;

    public GroqAdapter(IClock clock, IProviderErrorMapper errorMapper, ProviderHttpExecutor http)
        : base(clock, errorMapper)
    {
        _http = http;
    }

    public override ProviderDescriptor Descriptor { get; } = new(
        "groq",
        "Groq API",
        ProviderType.AiApi,
        ConnectionKind.ApiKey,
        ProviderCredentialKind.ApiKey,
        new HashSet<ProviderCapability>
        {
            ProviderCapability.ConnectionHealth,
            ProviderCapability.AvailableModels,
            ProviderCapability.RateLimits
        },
        "https://console.groq.com/docs/rate-limits",
        "API key access to official model metadata and response rate-limit headers.",
        new HashSet<string>(["api.groq.com"], StringComparer.OrdinalIgnoreCase));

    protected override async Task<ProviderOperationResult> CheckCapabilityAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var payload = await _http.GetJsonAsync("provider.groq", Descriptor.ProviderId, "/openai/v1/models", "Authorization", $"Bearer {credential.Value}", cancellationToken: cancellationToken);
        var root = payload.Document.RootElement;
        var observations = new List<ProviderObservationDraft>();
        var count = root.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array
            ? data.GetArrayLength()
            : 0;
        if (capability is ProviderCapability.ConnectionHealth or ProviderCapability.AvailableModels)
        {
            if (count == 0)
            {
                return ProviderOperationResult.Failure(ProviderOperationStatus.InvalidResponse, "GROQ_MODELS_RESPONSE_INVALID", "Groq returned no model metadata.", connection, DateTimeOffset.UtcNow, correlationId, capability);
            }

            observations.Add(Observation(ProviderCapability.AvailableModels, "model_count", count.ToString(CultureInfo.InvariantCulture), "models", ProviderObservationScope.Organization));
            return Success(connection, capability, correlationId, observations);
        }

        if (capability == ProviderCapability.RateLimits)
        {
            foreach (var header in new[] { "x-ratelimit-limit-requests", "x-ratelimit-remaining-requests", "x-ratelimit-reset-requests", "x-ratelimit-limit-tokens", "x-ratelimit-remaining-tokens", "x-ratelimit-reset-tokens" })
            {
                if (payload.Headers.TryGetValues(header, out var values))
                {
                    var value = values.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        observations.Add(Observation(ProviderCapability.RateLimits, header, value, "provider_header", ProviderObservationScope.IndividualKey));
                    }
                }
            }

            return observations.Count == 0
                ? Unsupported(connection, capability, correlationId)
                : Success(connection, capability, correlationId, observations);
        }

        return Unsupported(connection, capability, correlationId);
    }
}
