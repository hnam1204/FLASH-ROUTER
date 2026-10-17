using System.Globalization;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.Anthropic;

public sealed class AnthropicAdapter : ProviderAdapterBase
{
    private readonly ProviderHttpExecutor _http;

    public AnthropicAdapter(IClock clock, IProviderErrorMapper errorMapper, ProviderHttpExecutor http)
        : base(clock, errorMapper)
    {
        _http = http;
    }

    public override ProviderDescriptor Descriptor { get; } = new(
        "anthropic",
        "Anthropic Platform",
        ProviderType.AiApi,
        ConnectionKind.ApiKey,
        ProviderCredentialKind.ApiKey,
        new HashSet<ProviderCapability>
        {
            ProviderCapability.ConnectionHealth,
            ProviderCapability.AvailableModels,
            ProviderCapability.Usage,
            ProviderCapability.TokenUsage,
            ProviderCapability.Costs,
            ProviderCapability.RateLimits
        },
        "https://platform.claude.com/docs/en/manage-claude/usage-cost-api",
        "Organization usage and cost reports require an Anthropic Admin API credential.",
        new HashSet<string>(["api.anthropic.com"], StringComparer.OrdinalIgnoreCase));

    protected override async Task<ProviderOperationResult> CheckCapabilityAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (capability is ProviderCapability.Usage or ProviderCapability.TokenUsage or ProviderCapability.Costs
            && credential.CredentialKind != ProviderCredentialKind.AdminApiKey)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.Forbidden,
                "INSUFFICIENT_PERMISSION",
                "Anthropic usage and cost reports require an Admin API credential.",
                connection,
                DateTimeOffset.UtcNow,
                correlationId,
                capability);
        }

        return capability switch
        {
            ProviderCapability.ConnectionHealth or ProviderCapability.AvailableModels
                => await CheckModelsAsync(connection, credential, capability, correlationId, cancellationToken),
            ProviderCapability.Usage or ProviderCapability.TokenUsage
                => await CheckUsageAsync(connection, credential, capability, request, correlationId, cancellationToken),
            ProviderCapability.Costs
                => await CheckCostsAsync(connection, credential, capability, request, correlationId, cancellationToken),
            ProviderCapability.RateLimits => Unsupported(connection, capability, correlationId),
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
            "provider.anthropic",
            Descriptor.ProviderId,
            "/v1/models",
            "x-api-key",
            credential.Value,
            AnthropicHeaders,
            cancellationToken);
        var root = payload.Document.RootElement;
        var count = root.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array
            ? data.GetArrayLength()
            : 0;
        return count == 0
            ? ProviderOperationResult.Failure(ProviderOperationStatus.InvalidResponse, "ANTHROPIC_MODELS_RESPONSE_INVALID", "Anthropic returned no model metadata.", connection, DateTimeOffset.UtcNow, correlationId, capability)
            : Success(connection, capability, correlationId, [Observation(ProviderCapability.AvailableModels, "model_count", count.ToString(CultureInfo.InvariantCulture), "models", ProviderObservationScope.Organization)]);
    }

    private async Task<ProviderOperationResult> CheckUsageAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var query = ReportQuery(request);
        using var payload = await _http.GetJsonWithQueryAsync("provider.anthropic", Descriptor.ProviderId, "/v1/organizations/usage_report/messages", query, "x-api-key", credential.Value, AnthropicHeaders, cancellationToken);
        var observations = ParseReport(payload.Document.RootElement, capability, includeCost: false);
        return observations.Count == 0
            ? ProviderOperationResult.Failure(ProviderOperationStatus.InvalidResponse, "ANTHROPIC_USAGE_RESPONSE_EMPTY", "Anthropic returned no usage observations for the requested period.", connection, DateTimeOffset.UtcNow, correlationId, capability)
            : Success(connection, capability, correlationId, observations);
    }

    private async Task<ProviderOperationResult> CheckCostsAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var payload = await _http.GetJsonWithQueryAsync("provider.anthropic", Descriptor.ProviderId, "/v1/organizations/cost_report", ReportQuery(request), "x-api-key", credential.Value, AnthropicHeaders, cancellationToken);
        var observations = ParseReport(payload.Document.RootElement, capability, includeCost: true);
        return observations.Count == 0
            ? ProviderOperationResult.Failure(ProviderOperationStatus.InvalidResponse, "ANTHROPIC_COST_RESPONSE_EMPTY", "Anthropic returned no cost observations for the requested period.", connection, DateTimeOffset.UtcNow, correlationId, capability)
            : Success(connection, capability, correlationId, observations);
    }

    private static Dictionary<string, string> ReportQuery(ProviderCheckRequest request)
    {
        var end = request.EndUtc ?? DateTimeOffset.UtcNow;
        var start = request.StartUtc ?? end.AddDays(-1);
        if (start >= end || end - start > TimeSpan.FromDays(31))
        {
            throw new ProviderConfigurationException("TIME_RANGE_INVALID", "The provider report time range is invalid.");
        }

        return new Dictionary<string, string>
        {
            ["starting_at"] = start.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["ending_at"] = end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["bucket_width"] = "1d"
        };
    }

    private static IReadOnlyDictionary<string, string> AnthropicHeaders { get; } = new Dictionary<string, string>
    {
        ["anthropic-version"] = "2023-06-01"
    };

    private static List<ProviderObservationDraft> ParseReport(System.Text.Json.JsonElement root, ProviderCapability capability, bool includeCost)
    {
        var observations = new List<ProviderObservationDraft>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return observations;
        }

        foreach (var item in data.EnumerateArray())
        {
            var timestamp = ReadTimestamp(item, "starting_at", "start_time", "period_start");
            foreach (var field in new[] { "input_tokens", "output_tokens", "cache_read_input_tokens", "cache_creation_input_tokens" })
            {
                var value = ReadDecimal(item, field);
                if (value is not null && value >= 0)
                {
                    var suffix = timestamp?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "unknown";
                    observations.Add(Observation(capability, $"{field}_{suffix}", value.Value.ToString(CultureInfo.InvariantCulture), "tokens", ProviderObservationScope.Organization, providerReportedAtUtc: timestamp));
                }
            }

            if (includeCost)
            {
                var cost = ReadDecimal(item, "cost", "amount", "total_cost");
                if (cost is not null && cost >= 0)
                {
                    observations.Add(Observation(capability, "cost", cost.Value.ToString(CultureInfo.InvariantCulture), "currency_amount", ProviderObservationScope.Organization, ReadString(item, "currency") ?? "USD", providerReportedAtUtc: timestamp));
                }
            }
        }

        return observations;
    }
}
