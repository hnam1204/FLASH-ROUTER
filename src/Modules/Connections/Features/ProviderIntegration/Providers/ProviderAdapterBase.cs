using System.Globalization;
using System.Text.Json;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Providers;

public abstract class ProviderAdapterBase : IAiProviderAdapter
{
    private readonly IClock _clock;
    private readonly IProviderErrorMapper _errorMapper;

    protected ProviderAdapterBase(IClock clock, IProviderErrorMapper errorMapper)
    {
        _clock = clock;
        _errorMapper = errorMapper;
    }

    public abstract ProviderDescriptor Descriptor { get; }

    public async Task<ProviderCheckResult> CheckAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = _clock.UtcNow;
        var correlationId = Guid.NewGuid().ToString("N");
        if (!string.Equals(connection.ProviderId, credential.ProviderId, StringComparison.OrdinalIgnoreCase)
            || connection.ConnectionId != credential.ConnectionId
            || !string.Equals(connection.CredentialId, credential.CredentialId, StringComparison.Ordinal))
        {
            var mismatch = ProviderOperationResult.Failure(
                ProviderOperationStatus.ConfigurationError,
                "PROVIDER_CREDENTIAL_MISMATCH",
                "The credential is not bound to this provider connection; the provider was not contacted.",
                connection,
                _clock.UtcNow,
                correlationId);
            return new ProviderCheckResult(
                mismatch.Status,
                mismatch.SafeErrorCode,
                mismatch.SafeUserMessage,
                connection.ProviderId,
                connection.ConnectionId,
                startedAt,
                _clock.UtcNow,
                correlationId,
                [],
                [mismatch]);
        }

        var capabilities = request.Capabilities.Count == 0
            ? Descriptor.SupportedCapabilities
            : request.Capabilities;
        var operations = new List<ProviderOperationResult>();

        foreach (var capability in capabilities.OrderBy(value => value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Descriptor.SupportedCapabilities.Contains(capability))
            {
                operations.Add(ProviderOperationResult.Failure(
                    ProviderOperationStatus.Unsupported,
                    "CAPABILITY_UNSUPPORTED",
                    "This capability is not supported by the provider adapter.",
                    connection,
                    _clock.UtcNow,
                    correlationId,
                    capability));
                continue;
            }

            try
            {
                operations.Add(await CheckCapabilityAsync(
                    connection,
                    credential,
                    capability,
                    request,
                    correlationId,
                    cancellationToken));
            }
            catch (Exception exception)
            {
                operations.Add(_errorMapper.Map(
                    connection,
                    capability,
                    exception,
                    _clock.UtcNow,
                    correlationId));
            }
        }

        var observations = operations
            .SelectMany(operation => operation.SafeObservations)
            .ToArray();
        var status = operations.Count == 0
            ? ProviderOperationStatus.Unsupported
            : operations.All(operation => operation.IsSuccess)
                ? ProviderOperationStatus.Success
                : operations.Any(operation => operation.IsSuccess)
                    ? ProviderOperationStatus.PartialSuccess
                    : operations[0].Status;
        var firstFailure = operations.FirstOrDefault(operation => !operation.IsSuccess);
        return new ProviderCheckResult(
            status,
            firstFailure?.SafeErrorCode ?? string.Empty,
            firstFailure?.SafeUserMessage ?? string.Empty,
            connection.ProviderId,
            connection.ConnectionId,
            startedAt,
            _clock.UtcNow,
            correlationId,
            observations,
            operations);
    }

    protected abstract Task<ProviderOperationResult> CheckCapabilityAsync(
        ProviderConnectionContext connection,
        ProviderCredential credential,
        ProviderCapability capability,
        ProviderCheckRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    protected ProviderOperationResult Success(
        ProviderConnectionContext connection,
        ProviderCapability capability,
        string correlationId,
        IReadOnlyList<ProviderObservationDraft> observations)
        => ProviderOperationResult.Success(
            connection.ProviderId,
            connection.ConnectionId,
            _clock.UtcNow,
            correlationId,
            observations,
            capability);

    protected ProviderOperationResult Unsupported(
        ProviderConnectionContext connection,
        ProviderCapability capability,
        string correlationId)
        => ProviderOperationResult.Failure(
            ProviderOperationStatus.Unsupported,
            "CAPABILITY_UNSUPPORTED",
            "This capability is not supported for the selected provider connection.",
            connection,
            _clock.UtcNow,
            correlationId,
            capability);

    protected static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    protected static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(root, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return value.ToString();
            }
        }

        return null;
    }

    protected static decimal? ReadDecimal(JsonElement root, params string[] names)
    {
        var text = ReadString(root, names);
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    protected static DateTimeOffset? ReadTimestamp(JsonElement root, params string[] names)
    {
        var text = ReadString(root, names);
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            return timestamp.ToUniversalTime();
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            return unix > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                : DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        return null;
    }

    protected static ProviderObservationDraft Observation(
        ProviderCapability capability,
        string metric,
        string value,
        string unit,
        ProviderObservationScope scope,
        string? currency = null,
        ProviderObservationSource source = ProviderObservationSource.OfficialApi,
        DateTimeOffset? providerReportedAtUtc = null,
        DateTimeOffset? validUntilUtc = null,
        ProviderObservationStatus status = ProviderObservationStatus.Confirmed)
        => new(capability, metric, value, unit, currency, source, scope, providerReportedAtUtc, validUntilUtc, status);
}
