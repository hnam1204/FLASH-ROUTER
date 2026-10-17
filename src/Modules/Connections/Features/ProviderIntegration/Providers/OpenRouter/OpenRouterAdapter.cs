using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.OpenRouter;

public sealed class OpenRouterAdapter : ProviderAdapterBase
{
    private readonly ProviderHttpExecutor _http;

    public OpenRouterAdapter(IClock clock, IProviderErrorMapper errorMapper, ProviderHttpExecutor http)
        : base(clock, errorMapper)
    {
        _http = http;
    }

    public override ProviderDescriptor Descriptor { get; } = new(
        "openrouter",
        "OpenRouter",
        ProviderType.AiApi,
        ConnectionKind.ApiKey,
        ProviderCredentialKind.ApiKey,
        new HashSet<ProviderCapability>
        {
            ProviderCapability.Identity,
            ProviderCapability.ConnectionHealth,
            ProviderCapability.Usage,
            ProviderCapability.Credits,
            ProviderCapability.Balance,
            ProviderCapability.RateLimits
        },
        "https://openrouter.ai/docs/api/api-reference/api-keys/get-current-key",
        "API key for current-key metadata; management permission may be required for credits.",
        new HashSet<string>(["openrouter.ai"], StringComparer.OrdinalIgnoreCase));

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
            ProviderCapability.Credits or ProviderCapability.Balance
                => await CheckCreditsAsync(connection, credential, capability, correlationId, cancellationToken),
            ProviderCapability.Identity or ProviderCapability.ConnectionHealth or ProviderCapability.Usage or ProviderCapability.RateLimits
                => await CheckCurrentKeyAsync(connection, credential, capability, correlationId, cancellationToken),
            _ => Unsupported(connection, capability, correlationId)
        };
    }

    private async Task<ProviderOperationResult> CheckCurrentKeyAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var payload = await _http.GetJsonAsync(
            "provider.openrouter",
            Descriptor.ProviderId,
            "/api/v1/key",
            "Authorization",
            $"Bearer {credential.Value}",
            cancellationToken: cancellationToken);
        var root = payload.Document.RootElement;
        var data = TryGetProperty(root, "data", out var dataElement) ? dataElement : root;
        var observations = new List<ProviderObservationDraft>();
        AddString(data, observations, capability, "label", "label", "text", ProviderObservationScope.IndividualKey);
        AddDecimal(data, observations, capability, "usage", "usage", "credits", ProviderObservationScope.IndividualKey, currency: null);
        AddDecimal(data, observations, capability, "limit", "limit", "credits", ProviderObservationScope.IndividualKey, currency: null);
        AddDecimal(data, observations, capability, "limit_remaining", "limit_remaining", "credits", ProviderObservationScope.IndividualKey, currency: null);
        AddDecimal(data, observations, capability, "usage_daily", "usage_daily", "credits", ProviderObservationScope.IndividualKey, currency: null);
        AddDecimal(data, observations, capability, "usage_weekly", "usage_weekly", "credits", ProviderObservationScope.IndividualKey, currency: null);
        AddDecimal(data, observations, capability, "usage_monthly", "usage_monthly", "credits", ProviderObservationScope.IndividualKey, currency: null);
        AddString(data, observations, capability, "limit_reset", "limit_reset", "timestamp", ProviderObservationScope.IndividualKey);
        AddString(data, observations, capability, "expires_at", "expires_at", "timestamp", ProviderObservationScope.IndividualKey);
        if (observations.Count == 0)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.InvalidResponse,
                "OPENROUTER_KEY_RESPONSE_INVALID",
                "OpenRouter returned no recognized key metadata.",
                connection,
                DateTimeOffset.UtcNow,
                correlationId,
                capability);
        }

        return Success(connection, capability, correlationId, observations);
    }

    private async Task<ProviderOperationResult> CheckCreditsAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var payload = await _http.GetJsonAsync(
            "provider.openrouter",
            Descriptor.ProviderId,
            "/api/v1/credits",
            "Authorization",
            $"Bearer {credential.Value}",
            cancellationToken: cancellationToken);
        var root = payload.Document.RootElement;
        var data = TryGetProperty(root, "data", out var dataElement) ? dataElement : root;
        var observations = new List<ProviderObservationDraft>();
        AddDecimal(data, observations, capability, "total_credits", "total_credits", "credits", ProviderObservationScope.BillingAccount, "USD");
        AddDecimal(data, observations, capability, "total_usage", "total_usage", "credits", ProviderObservationScope.BillingAccount, "USD");
        var totalCredits = ReadDecimal(data, "total_credits");
        var totalUsage = ReadDecimal(data, "total_usage");
        if (totalCredits is not null && totalUsage is not null && totalCredits >= totalUsage)
        {
            observations.Add(Observation(
                ProviderCapability.Balance,
                "remaining_credits",
                (totalCredits.Value - totalUsage.Value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "credits",
                ProviderObservationScope.BillingAccount,
                "USD",
                ProviderObservationSource.CalculatedFromOfficialObservations));
        }

        return observations.Count == 0
            ? ProviderOperationResult.Failure(
                ProviderOperationStatus.InvalidResponse,
                "OPENROUTER_CREDITS_RESPONSE_INVALID",
                "OpenRouter returned no recognized credit metadata.",
                connection,
                DateTimeOffset.UtcNow,
                correlationId,
                capability)
            : Success(connection, capability, correlationId, observations);
    }

    private static void AddString(
        System.Text.Json.JsonElement root,
        List<ProviderObservationDraft> observations,
        ProviderCapability capability,
        string property,
        string metric,
        string unit,
        ProviderObservationScope scope)
    {
        var value = ReadString(root, property);
        if (!string.IsNullOrWhiteSpace(value))
        {
            observations.Add(Observation(capability, metric, value, unit, scope));
        }
    }

    private static void AddDecimal(
        System.Text.Json.JsonElement root,
        List<ProviderObservationDraft> observations,
        ProviderCapability capability,
        string property,
        string metric,
        string unit,
        ProviderObservationScope scope,
        string? currency)
    {
        var value = ReadDecimal(root, property);
        if (value is not null && value >= 0)
        {
            observations.Add(Observation(
                capability,
                metric,
                value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                unit,
                scope,
                currency));
        }
    }
}
