using System.Globalization;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Gemini;

public sealed class GeminiAdapter : ProviderAdapterBase
{
    private readonly ProviderHttpExecutor _http;

    public GeminiAdapter(IClock clock, IProviderErrorMapper errorMapper, ProviderHttpExecutor http)
        : base(clock, errorMapper)
    {
        _http = http;
    }

    public override ProviderDescriptor Descriptor { get; } = new(
        "gemini",
        "Google Gemini API",
        ProviderType.AiApi,
        ConnectionKind.ApiKey,
        ProviderCredentialKind.ApiKey,
        new HashSet<ProviderCapability>
        {
            ProviderCapability.ConnectionHealth,
            ProviderCapability.AvailableModels,
            ProviderCapability.RateLimits
        },
        "https://ai.google.dev/gemini-api/docs/rate-limits",
        "Gemini API key reads official model metadata; project quota and billing require separate Google Cloud permissions.",
        new HashSet<string>(["generativelanguage.googleapis.com"], StringComparer.OrdinalIgnoreCase));

    protected override async Task<ProviderOperationResult> CheckCapabilityAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        return capability switch
        {
            ProviderCapability.ConnectionHealth or ProviderCapability.AvailableModels
                => await CheckModelsAsync(connection, credential, capability, correlationId, cancellationToken),
            ProviderCapability.RateLimits
                => Unsupported(connection, capability, correlationId),
            _ => Unsupported(connection, capability, correlationId)
        };
    }

    private async Task<ProviderOperationResult> CheckModelsAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var payload = await _http.GetJsonAsync(
            "provider.gemini",
            Descriptor.ProviderId,
            "/v1beta/models",
            "x-goog-api-key",
            credential.Value,
            cancellationToken: cancellationToken);
        var root = payload.Document.RootElement;
        var count = root.TryGetProperty("models", out var models) && models.ValueKind == System.Text.Json.JsonValueKind.Array
            ? models.GetArrayLength()
            : 0;
        return count == 0
            ? ProviderOperationResult.Failure(ProviderOperationStatus.InvalidResponse, "GEMINI_MODELS_RESPONSE_INVALID", "Google Gemini returned no model metadata.", connection, DateTimeOffset.UtcNow, correlationId, capability)
            : Success(connection, capability, correlationId, [Observation(ProviderCapability.AvailableModels, "model_count", count.ToString(CultureInfo.InvariantCulture), "models", ProviderObservationScope.Project)]);
    }
}
