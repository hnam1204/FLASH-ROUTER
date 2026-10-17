using System.Collections.Concurrent;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

public sealed class ProviderSyncOrchestrator : IProviderSyncOrchestrator
{
    private readonly IProviderConnectionCatalog _connections;
    private readonly IProviderConnectionVerifier _verifier;
    private readonly IProviderObservationWriter _writer;
    private readonly IProviderRegistry _registry;
    private readonly IProviderCapabilityResolver _capabilities;
    private readonly ProviderRateLimiter _rateLimiter;

    public ProviderSyncOrchestrator(
        IProviderConnectionCatalog connections,
        IProviderConnectionVerifier verifier,
        IProviderObservationWriter writer,
        IProviderRegistry registry,
        IProviderCapabilityResolver capabilities,
        ProviderRateLimiter rateLimiter)
    {
        _connections = connections;
        _verifier = verifier;
        _writer = writer;
        _registry = registry;
        _capabilities = capabilities;
        _rateLimiter = rateLimiter;
    }

    public async Task<ProviderCheckRunSummary> CheckConnectionAsync(
        Guid connectionId,
        IReadOnlySet<ProviderCapability>? capabilities = null,
        bool allowBillableRead = false,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetAsync(connectionId, cancellationToken)
            ?? throw new ProviderConfigurationException("CONNECTION_NOT_FOUND", "The provider connection was not found.");
        var descriptor = _registry.Resolve(connection.ProviderId).Descriptor;
        var selected = capabilities ?? _capabilities.Resolve(connection, descriptor);
        if (!allowBillableRead)
        {
            selected = selected.Where(capability => capability is not ProviderCapability.Costs and not ProviderCapability.Usage and not ProviderCapability.TokenUsage).ToHashSet();
        }

        using var lease = await _rateLimiter.EnterAsync(connection.ProviderId, cancellationToken);
        var result = await VerifyWithBoundedRetryAsync(
            connection,
            new ProviderCheckRequest(selected, AllowBillableRead: allowBillableRead),
            cancellationToken);
        var summary = await _writer.SaveAsync(result, cancellationToken);
        await _connections.MarkStatusAsync(connectionId, MapStatus(result.Status), cancellationToken);
        return summary;
    }

    public async Task<CheckAllPreview> PreviewCheckAllAsync(CancellationToken cancellationToken = default)
    {
        var connections = await _connections.GetEligibleAsync(cancellationToken);
        var capabilities = new Dictionary<string, IReadOnlySet<ProviderCapability>>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in connections)
        {
            if (!_registry.TryResolve(connection.ProviderId, out var adapter) || adapter is null)
            {
                continue;
            }

            var context = await _connections.GetAsync(connection.ConnectionId, cancellationToken);
            if (context is not null)
            {
                capabilities[connection.ProviderId] = _capabilities.Resolve(context, adapter.Descriptor);
            }
        }

        var billingRisk = capabilities.Values.Any(set => set.Contains(ProviderCapability.Costs) || set.Contains(ProviderCapability.Usage));
        return new CheckAllPreview(connections, capabilities, billingRisk, connections.Count == 0 ? "No authorized API connections are available." : "Only stored authorized API connections will be checked.");
    }

    public async Task<CheckAllResult> CheckAllAsync(
        CheckAllRequest request,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!request.Confirmed)
        {
            return new CheckAllResult([], false, "Explicit confirmation is required before checking connections.");
        }

        var results = new List<ProviderCheckRunSummary>();
        try
        {
            var preview = await PreviewCheckAllAsync(cancellationToken);
            for (var index = 0; index < preview.EligibleConnections.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var connection = preview.EligibleConnections[index];
                var capabilities = request.Capabilities;
                var result = await CheckConnectionAsync(connection.ConnectionId, capabilities, request.AllowBillableRead, cancellationToken);
                results.Add(result);
                progress?.Report((index + 1) * 100 / preview.EligibleConnections.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CheckAllResult(results, true, "The provider check-all operation was cancelled; completed results were preserved.");
        }

        return new CheckAllResult(results, false, results.Count == 0 ? "No eligible connections were checked." : "Authorized connections were checked with per-provider throttling.");
    }

    private async Task<ProviderCheckResult> VerifyWithBoundedRetryAsync(
        ProviderConnectionContext connection,
        ProviderCheckRequest request,
        CancellationToken cancellationToken)
    {
        ProviderCheckResult? result = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            result = await _verifier.VerifyAsync(connection, request, cancellationToken);
            if (!IsTransient(result.Status) || attempt == 2)
            {
                return result;
            }

            var retryAfter = result.Operations
                .Select(operation => operation.RetryAfterUtc)
                .Where(value => value is not null)
                .Select(value => value!.Value - DateTimeOffset.UtcNow)
                .Where(value => value > TimeSpan.Zero)
                .OrderBy(value => value)
                .FirstOrDefault();
            var delay = retryAfter == default
                ? TimeSpan.FromMilliseconds(100 * (attempt + 1))
                : TimeSpan.FromMilliseconds(Math.Min(1000, retryAfter.TotalMilliseconds));
            await Task.Delay(delay, cancellationToken);
        }

        return result!;
    }

    private static bool IsTransient(ProviderOperationStatus status)
        => status is ProviderOperationStatus.RateLimited
            or ProviderOperationStatus.Timeout
            or ProviderOperationStatus.NetworkFailure
            or ProviderOperationStatus.ProviderUnavailable;

    private static ConnectionStatus MapStatus(ProviderOperationStatus status)
        => status switch
        {
            ProviderOperationStatus.Success or ProviderOperationStatus.PartialSuccess => ConnectionStatus.Authorized,
            ProviderOperationStatus.Unauthorized => ConnectionStatus.Unauthorized,
            ProviderOperationStatus.RateLimited => ConnectionStatus.RateLimited,
            ProviderOperationStatus.NetworkFailure => ConnectionStatus.NetworkError,
            ProviderOperationStatus.Timeout or ProviderOperationStatus.ProviderUnavailable => ConnectionStatus.VerificationUnavailable,
            ProviderOperationStatus.Unsupported => ConnectionStatus.VerificationUnavailable,
            _ => ConnectionStatus.Error
        };
}

public sealed class ProviderRateLimiter
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> EnterAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(providerId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;

        public Lease(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate.Release();
            }
        }
    }
}
