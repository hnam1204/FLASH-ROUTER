using System.Globalization;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.OpenAIPlatform;

public sealed class OpenAiPlatformAdapter : ProviderAdapterBase
{
    private readonly ProviderHttpExecutor _http;

    public OpenAiPlatformAdapter(IClock clock, IProviderErrorMapper errorMapper, ProviderHttpExecutor http)
        : base(clock, errorMapper)
    {
        _http = http;
    }

    public override ProviderDescriptor Descriptor { get; } = new(
        "openai-platform",
        "OpenAI Platform",
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
            ProviderCapability.BillingPeriod
        },
        "https://platform.openai.com/docs/api-reference/usage",
        "Standard keys can read model metadata; organization usage and costs require an admin credential.",
        new HashSet<string>(["api.openai.com"], StringComparer.OrdinalIgnoreCase));

    protected override async Task<ProviderOperationResult> CheckCapabilityAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (capability is ProviderCapability.Usage or ProviderCapability.TokenUsage or ProviderCapability.Costs or ProviderCapability.BillingPeriod
            && credential.CredentialKind is not ProviderCredentialKind.AdminApiKey and not ProviderCredentialKind.ProjectApiKey)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.Forbidden,
                "INSUFFICIENT_PERMISSION",
                "This OpenAI Platform report requires an organization or project-scoped administrative credential.",
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
            ProviderCapability.Costs or ProviderCapability.BillingPeriod
                => await CheckCostsAsync(connection, credential, capability, request, correlationId, cancellationToken),
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
            "provider.openai-platform",
            Descriptor.ProviderId,
            "/v1/models",
            "Authorization",
            $"Bearer {credential.Value}",
            ScopeHeaders(connection, credential),
            cancellationToken);
        var root = payload.Document.RootElement;
        var count = root.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array
            ? data.GetArrayLength()
            : 0;
        return count == 0
            ? ProviderOperationResult.Failure(
                ProviderOperationStatus.InvalidResponse,
                "OPENAI_MODELS_RESPONSE_INVALID",
                "OpenAI Platform returned no model metadata.",
                connection,
                DateTimeOffset.UtcNow,
                correlationId,
                capability)
            : Success(connection, capability, correlationId, [Observation(
                ProviderCapability.AvailableModels,
                "model_count",
                count.ToString(CultureInfo.InvariantCulture),
                "models",
                ProviderObservationScope.Organization)]);
    }

    private async Task<ProviderOperationResult> CheckUsageAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (start, end) = BoundedRange(request);
        using var payload = await _http.GetJsonWithQueryAsync(
            "provider.openai-platform",
            Descriptor.ProviderId,
            "/v1/organization/usage/completions",
            new Dictionary<string, string>
            {
                ["start_time"] = start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["end_time"] = end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["bucket_width"] = "1d"
            },
            "Authorization",
            $"Bearer {credential.Value}",
            ScopeHeaders(connection, credential),
            cancellationToken);
        var observations = new List<ProviderObservationDraft>();
        ParseUsage(payload.Document.RootElement, observations, capability);
        return observations.Count == 0
            ? ProviderOperationResult.Failure(
                ProviderOperationStatus.InvalidResponse,
                "OPENAI_USAGE_RESPONSE_EMPTY",
                "OpenAI Platform returned no usage buckets for the requested period.",
                connection,
                DateTimeOffset.UtcNow,
                correlationId,
                capability)
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
        var (start, end) = BoundedRange(request);
        using var payload = await _http.GetJsonWithQueryAsync(
            "provider.openai-platform",
            Descriptor.ProviderId,
            "/v1/organization/costs",
            new Dictionary<string, string>
            {
                ["start_time"] = start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["end_time"] = end.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ["bucket_width"] = "1d"
            },
            "Authorization",
            $"Bearer {credential.Value}",
            ScopeHeaders(connection, credential),
            cancellationToken);
        var observations = new List<ProviderObservationDraft>();
        ParseCosts(payload.Document.RootElement, observations, capability);
        return observations.Count == 0
            ? ProviderOperationResult.Failure(
                ProviderOperationStatus.InvalidResponse,
                "OPENAI_COST_RESPONSE_EMPTY",
                "OpenAI Platform returned no cost buckets for the requested period.",
                connection,
                DateTimeOffset.UtcNow,
                correlationId,
                capability)
            : Success(connection, capability, correlationId, observations);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) BoundedRange(ProviderCheckRequest request)
    {
        var end = request.EndUtc ?? DateTimeOffset.UtcNow;
        var start = request.StartUtc ?? end.AddDays(-1);
        if (start >= end || end - start > TimeSpan.FromDays(31))
        {
            throw new ProviderConfigurationException("TIME_RANGE_INVALID", "The provider report time range is invalid.");
        }

        return (start.ToUniversalTime(), end.ToUniversalTime());
    }

    private static Dictionary<string, string>? ScopeHeaders(ProviderConnectionContext connection, ProviderCredential credential)
    {
        if (string.IsNullOrWhiteSpace(connection.Scope))
        {
            return null;
        }

        var header = credential.CredentialKind == ProviderCredentialKind.ProjectApiKey
            ? "OpenAI-Project"
            : "OpenAI-Organization";
        return new Dictionary<string, string> { [header] = connection.Scope };
    }

    private static void ParseUsage(
        System.Text.Json.JsonElement root,
        List<ProviderObservationDraft> observations,
        ProviderCapability capability)
    {
        if (!root.TryGetProperty("data", out var buckets) || buckets.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return;
        }

        foreach (var bucket in buckets.EnumerateArray())
        {
            var timestamp = ReadTimestamp(bucket, "start_time", "start_time_seconds", "start");
            if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                AddUsage(result, observations, capability, "input_tokens", "input_tokens", "tokens", timestamp);
                AddUsage(result, observations, capability, "output_tokens", "output_tokens", "tokens", timestamp);
                AddUsage(result, observations, capability, "num_model_requests", "request_count", "requests", timestamp);
            }
        }
    }

    private static void AddUsage(
        System.Text.Json.JsonElement root,
        List<ProviderObservationDraft> observations,
        ProviderCapability capability,
        string property,
        string metric,
        string unit,
        DateTimeOffset? timestamp)
    {
        var value = ReadDecimal(root, property);
        if (value is not null && value >= 0)
        {
            var suffix = timestamp?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "unknown";
            observations.Add(Observation(capability, $"{metric}_{suffix}", value.Value.ToString(CultureInfo.InvariantCulture), unit, ProviderObservationScope.Organization, providerReportedAtUtc: timestamp));
        }
    }

    private static void ParseCosts(
        System.Text.Json.JsonElement root,
        List<ProviderObservationDraft> observations,
        ProviderCapability capability)
    {
        if (!root.TryGetProperty("data", out var buckets) || buckets.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return;
        }

        foreach (var bucket in buckets.EnumerateArray())
        {
            var timestamp = ReadTimestamp(bucket, "start_time", "start");
            if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                var amount = result.TryGetProperty("amount", out var amountElement) ? amountElement : result;
                var value = ReadDecimal(amount, "value", "amount");
                if (value is null || value < 0)
                {
                    continue;
                }

                var currency = ReadString(amount, "currency") ?? "USD";
                var suffix = timestamp?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "unknown";
                observations.Add(Observation(capability, $"cost_{suffix}", value.Value.ToString(CultureInfo.InvariantCulture), "currency_amount", ProviderObservationScope.Organization, currency, providerReportedAtUtc: timestamp));
            }
        }
    }
}
