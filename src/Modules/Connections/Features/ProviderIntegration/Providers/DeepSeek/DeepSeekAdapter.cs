using System.Globalization;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Providers.DeepSeek;

public sealed class DeepSeekAdapter : ProviderAdapterBase
{
    private readonly ProviderHttpExecutor _http;

    public DeepSeekAdapter(IClock clock, IProviderErrorMapper errorMapper, ProviderHttpExecutor http)
        : base(clock, errorMapper)
    {
        _http = http;
    }

    public override ProviderDescriptor Descriptor { get; } = new(
        "deepseek",
        "DeepSeek API",
        ProviderType.AiApi,
        ConnectionKind.ApiKey,
        ProviderCredentialKind.ApiKey,
        new HashSet<ProviderCapability>
        {
            ProviderCapability.ConnectionHealth,
            ProviderCapability.Balance,
            ProviderCapability.Credits
        },
        "https://api-docs.deepseek.com/api/get-user-balance",
        "API key access to the official user balance endpoint.",
        new HashSet<string>(["api.deepseek.com"], StringComparer.OrdinalIgnoreCase));

    protected override async Task<ProviderOperationResult> CheckCapabilityAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (capability == ProviderCapability.ConnectionHealth)
        {
            return await CheckBalanceAsync(connection, credential, capability, correlationId, cancellationToken);
        }

        if (capability is ProviderCapability.Balance or ProviderCapability.Credits)
        {
            return await CheckBalanceAsync(connection, credential, capability, correlationId, cancellationToken);
        }

        return Unsupported(connection, capability, correlationId);
    }

    private async Task<ProviderOperationResult> CheckBalanceAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var payload = await _http.GetJsonAsync("provider.deepseek", Descriptor.ProviderId, "/user/balance", "Authorization", $"Bearer {credential.Value}", cancellationToken: cancellationToken);
        var root = payload.Document.RootElement;
        var balances = root.TryGetProperty("balance_infos", out var balanceInfos) && balanceInfos.ValueKind == System.Text.Json.JsonValueKind.Array
            ? balanceInfos
            : default;
        var observations = new List<ProviderObservationDraft>();
        if (balances.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in balances.EnumerateArray())
            {
                var total = ReadDecimal(item, "total_balance");
                var granted = ReadDecimal(item, "granted_balance");
                var toppedUp = ReadDecimal(item, "topped_up_balance");
                var currency = ReadString(item, "currency") ?? "UNKNOWN";
                if (total is not null && total >= 0)
                {
                    observations.Add(Observation(capability, $"total_balance_{currency}", total.Value.ToString(CultureInfo.InvariantCulture), "currency_amount", ProviderObservationScope.BillingAccount, currency));
                }
                if (granted is not null && granted >= 0)
                {
                    observations.Add(Observation(capability, $"granted_balance_{currency}", granted.Value.ToString(CultureInfo.InvariantCulture), "currency_amount", ProviderObservationScope.BillingAccount, currency));
                }
                if (toppedUp is not null && toppedUp >= 0)
                {
                    observations.Add(Observation(capability, $"topped_up_balance_{currency}", toppedUp.Value.ToString(CultureInfo.InvariantCulture), "currency_amount", ProviderObservationScope.BillingAccount, currency));
                }
            }
        }

        return observations.Count == 0
            ? ProviderOperationResult.Failure(ProviderOperationStatus.InvalidResponse, "DEEPSEEK_BALANCE_RESPONSE_INVALID", "DeepSeek returned no recognized balance observations.", connection, DateTimeOffset.UtcNow, correlationId, capability)
            : Success(connection, capability, correlationId, observations);
    }
}
