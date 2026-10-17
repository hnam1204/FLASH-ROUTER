using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.Modules.Connections.Application;

public sealed class ConnectionObservationsReader : IConnectionObservationsReader
{
    private readonly IAccountConnectionsReader _connections;
    private readonly IProviderObservationReader _observations;

    public ConnectionObservationsReader(
        IAccountConnectionsReader connections,
        IProviderObservationReader observations)
    {
        _connections = connections;
        _observations = observations;
    }

    public async Task<IReadOnlyList<ConnectionObservationUiSnapshot>> ListForAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var connections = await _connections.ListForAccountAsync(accountId, cancellationToken);
        var results = new List<ConnectionObservationUiSnapshot>();

        foreach (var connection in connections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = await _observations.GetLatestAsync(connection.ConnectionId, cancellationToken);
            var history = await _observations.GetHistoryAsync(connection.ConnectionId, 1, cancellationToken);
            var latestCheck = history.FirstOrDefault();

            results.AddRange(latest.Select(observation => new ConnectionObservationUiSnapshot(
                accountId,
                connection.ConnectionId,
                connection.ProviderId,
                connection.ProviderDisplayName,
                observation.MetricName,
                observation.Value,
                observation.Unit,
                observation.Currency,
                observation.Scope.ToString(),
                observation.Source.ToString(),
                observation.ObservedAtUtc,
                observation.ProviderReportedAtUtc,
                observation.ValidUntilUtc,
                observation.Status.ToString(),
                latestCheck?.Status.ToString(),
                latestCheck?.SafeUserMessage,
                observation.Status == ProviderObservationStatus.Stale
                    || observation.ValidUntilUtc is not null && observation.ValidUntilUtc <= DateTimeOffset.UtcNow)));

            if (latest.Count == 0 && latestCheck is not null)
            {
                results.Add(new ConnectionObservationUiSnapshot(
                    accountId,
                    connection.ConnectionId,
                    connection.ProviderId,
                    connection.ProviderDisplayName,
                    "provider_check",
                    latestCheck.Status.ToString(),
                    string.Empty,
                    null,
                    ProviderObservationScope.Unknown.ToString(),
                    ProviderObservationSource.LocalMetadata.ToString(),
                    latestCheck.CompletedAtUtc,
                    null,
                    null,
                    ToUiStatus(latestCheck.Status),
                    latestCheck.Status.ToString(),
                    latestCheck.SafeUserMessage,
                    false));
            }
        }

        return results
            .OrderByDescending(item => item.ObservedAtUtc)
            .ThenBy(item => item.ProviderDisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.MetricName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ToUiStatus(ProviderOperationStatus status)
        => status switch
        {
            ProviderOperationStatus.Unsupported => "Unsupported",
            ProviderOperationStatus.Forbidden => "RequiresPermission",
            ProviderOperationStatus.RateLimited => "RateLimited",
            ProviderOperationStatus.Success => "Unavailable",
            _ => "Error"
        };
}
