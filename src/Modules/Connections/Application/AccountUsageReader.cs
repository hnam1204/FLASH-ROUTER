using System.Globalization;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

namespace FlashAI.Modules.Connections.Application;

public sealed class AccountUsageReader : IAccountUsageReader
{
    private static readonly ProviderCapability[] UsageCapabilities =
    [
        ProviderCapability.Usage,
        ProviderCapability.TokenUsage,
        ProviderCapability.Costs,
        ProviderCapability.Credits,
        ProviderCapability.Balance,
        ProviderCapability.RateLimits,
        ProviderCapability.Quota
    ];

    private readonly IAccountConnectionsReader _connections;
    private readonly IProviderObservationReader _observations;
    private readonly IProviderSyncStateStore _syncStates;
    private readonly IProviderRegistry _registry;

    public AccountUsageReader(
        IAccountConnectionsReader connections,
        IProviderObservationReader observations,
        IProviderSyncStateStore syncStates,
        IProviderRegistry registry)
    {
        _connections = connections;
        _observations = observations;
        _syncStates = syncStates;
        _registry = registry;
    }

    public async Task<AccountUsageView> GetAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var connections = await _connections.ListForAccountAsync(accountId, cancellationToken);
        var snapshots = new List<AccountUsageSnapshot>();
        var observedAt = DateTimeOffset.UtcNow;

        foreach (var connection in connections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = await _observations.GetLatestAsync(connection.ConnectionId, cancellationToken);
            var history = await _observations.GetHistoryAsync(connection.ConnectionId, 1, cancellationToken);
            var lastCheck = history.FirstOrDefault();
            var sync = await _syncStates.GetAsync(connection.ConnectionId, cancellationToken);

            var usageGroups = latest
                .Where(observation => UsageCapabilities.Contains(observation.Capability))
                .GroupBy(observation => new { observation.Capability, observation.Scope })
                .ToArray();

            foreach (var group in usageGroups)
            {
                var values = group.ToArray();
                var used = Parse(values, "usage", "total_usage", "used");
                var limit = Parse(values, "limit", "quota_limit", "total_credits");
                var remaining = Parse(values, "limit_remaining", "remaining", "quota_remaining", "balance");
                var percentage = used is not null && limit is > 0
                    ? (decimal?)(used.Value / limit.Value * 100m)
                    : null;
                var resetAt = ParseTimestamp(values, "limit_reset", "reset_at", "reset");
                var latestObservation = values.OrderByDescending(item => item.ObservedAtUtc).First();
                var unit = ResolveUnit(values);
                var status = ToUsageStatus(latestObservation.Status, lastCheck?.Status);
                observedAt = observedAt > latestObservation.ObservedAtUtc ? observedAt : latestObservation.ObservedAtUtc;
                snapshots.Add(new AccountUsageSnapshot(
                    accountId,
                    connection.ConnectionId,
                    connection.ProviderId,
                    group.Key.Capability,
                    group.Key.Scope.ToString(),
                    status,
                    used,
                    limit,
                    remaining,
                    percentage,
                    unit,
                    resetAt,
                    latestObservation.ObservedAtUtc,
                    sync?.NextEligibleAtUtc,
                    latestObservation.Source + "; " + latestObservation.Scope,
                    lastCheck is { Status: not ProviderOperationStatus.Success and not ProviderOperationStatus.PartialSuccess }
                        ? lastCheck.SafeErrorCode
                        : null,
                    latestObservation.Status == ProviderObservationStatus.Stale
                        || latestObservation.ValidUntilUtc is not null && latestObservation.ValidUntilUtc <= DateTimeOffset.UtcNow));
            }

            if (usageGroups.Length == 0)
            {
                var descriptor = _registry.Descriptors.FirstOrDefault(item => item.ProviderId.Equals(connection.ProviderId, StringComparison.OrdinalIgnoreCase));
                var isChatGptWeb = connection.ProviderId.Equals("openai-chatgpt", StringComparison.OrdinalIgnoreCase)
                    && connection.ConnectionKind == ConnectionKind.Manual;
                if (isChatGptWeb)
                {
                    snapshots.Add(Unsupported(accountId, connection, sync?.NextEligibleAtUtc, "No supported official web quota source"));
                }
                else if (descriptor is not null)
                {
                    foreach (var capability in UsageCapabilities.Where(descriptor.SupportedCapabilities.Contains))
                    {
                        snapshots.Add(new AccountUsageSnapshot(
                            accountId,
                            connection.ConnectionId,
                            connection.ProviderId,
                            capability,
                            connection.Scope ?? ProviderObservationScope.Unknown.ToString(),
                            "NotChecked",
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            connection.LastCheckedAtUtc ?? connection.CreatedAtUtc,
                            sync?.NextEligibleAtUtc,
                            descriptor.DocumentationReference,
                            lastCheck?.SafeErrorCode,
                            false));
                    }
                }
            }
        }

        return new AccountUsageView(accountId, snapshots.OrderBy(item => item.ProviderId).ThenBy(item => item.Capability).ToArray(), observedAt);
    }

    private static AccountUsageSnapshot Unsupported(
        Guid accountId,
        AccountConnectionSummary connection,
        DateTimeOffset? nextCheckAtUtc,
        string evidence)
        => new(
            accountId,
            connection.ConnectionId,
            connection.ProviderId,
            ProviderCapability.Usage,
            ProviderObservationScope.Unknown.ToString(),
            "Unsupported",
            null,
            null,
            null,
            null,
            null,
            null,
            connection.LastCheckedAtUtc ?? connection.CreatedAtUtc,
            nextCheckAtUtc,
            evidence,
            "USAGE_SOURCE_UNSUPPORTED",
            false);

    private static decimal? Parse(
        IReadOnlyList<ProviderObservationSnapshot> observations,
        params string[] metricNames)
    {
        foreach (var name in metricNames)
        {
            var observation = observations.FirstOrDefault(item => item.MetricName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (observation is not null && decimal.TryParse(observation.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseTimestamp(
        IReadOnlyList<ProviderObservationSnapshot> observations,
        params string[] metricNames)
    {
        foreach (var name in metricNames)
        {
            var observation = observations.FirstOrDefault(item => item.MetricName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (observation is not null && DateTimeOffset.TryParse(observation.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            {
                return value.ToUniversalTime();
            }
        }

        return null;
    }

    private static string? ResolveUnit(IReadOnlyList<ProviderObservationSnapshot> observations)
    {
        foreach (var name in new[] { "usage", "total_usage", "used", "limit", "quota_limit", "total_credits", "limit_remaining", "remaining", "quota_remaining", "balance" })
        {
            var observation = observations.FirstOrDefault(item => item.MetricName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (observation is not null && !string.IsNullOrWhiteSpace(observation.Unit))
            {
                return observation.Unit;
            }
        }

        return observations.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Unit))?.Unit;
    }

    private static string ToUsageStatus(ProviderObservationStatus observation, ProviderOperationStatus? check)
        => observation switch
        {
            ProviderObservationStatus.Confirmed or ProviderObservationStatus.Derived => "Available",
            ProviderObservationStatus.Unsupported => "Unsupported",
            ProviderObservationStatus.Stale => "Stale",
            ProviderObservationStatus.Unavailable => "Unavailable",
            ProviderObservationStatus.Error => check == ProviderOperationStatus.RateLimited ? "RateLimited" : "Failed",
            _ => "Unknown"
        };
}
