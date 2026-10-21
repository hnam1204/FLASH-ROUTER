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
    private readonly IProviderAccountObservationReader? _accountObservations;

    public AccountUsageReader(
        IAccountConnectionsReader connections,
        IProviderObservationReader observations,
        IProviderSyncStateStore syncStates,
        IProviderRegistry registry,
        IProviderAccountObservationReader? accountObservations = null)
    {
        _connections = connections;
        _observations = observations;
        _syncStates = syncStates;
        _registry = registry;
        _accountObservations = accountObservations;
    }

    public async Task<AccountUsageView> GetAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var connections = await _connections.ListForAccountAsync(accountId, cancellationToken);
        var snapshots = new List<AccountUsageSnapshot>();
        var observedAt = DateTimeOffset.UtcNow;
        var chatGptObservation = _accountObservations is null
            ? null
            : await _accountObservations.GetLatestAsync(accountId, "openai-chatgpt", cancellationToken);

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
                var directUsedPercentage = ParsePercentage(values, "used_percent");
                string? percentageFailureCode = null;
                var usedPercentage = directUsedPercentage ?? CalculatePercentage(values, used, limit, out percentageFailureCode);
                var directRemainingPercentage = ParsePercentage(values, "remaining_percent");
                string? remainingPercentageFailureCode = null;
                var remainingPercentage = directRemainingPercentage
                    ?? (directUsedPercentage.HasValue && usedPercentage is decimal usedPercent
                        ? 100m - usedPercent
                        : CalculatePercentage(
                        values,
                        remaining,
                        limit,
                        out remainingPercentageFailureCode,
                        "limit_remaining",
                        "remaining",
                        "quota_remaining",
                        "balance"));
                var percentage = usedPercentage;
                var windowStartedAt = ParseTimestamp(values, "window_start", "window_started_at", "period_start");
                var latestObservation = values.OrderByDescending(item => item.ObservedAtUtc).First();
                var resetAt = ParseTimestamp(values, "reset_at", "limit_reset", "reset")
                    ?? ParseResetAfter(values, latestObservation.ObservedAtUtc);
                var unit = ResolveUnit(values);
                var providerIdentity = ResolveProviderIdentity(connection, values);
                var bindingStatus = ResolveIdentityBinding(connection, providerIdentity);
                var isStale = latestObservation.Status == ProviderObservationStatus.Stale
                    || latestObservation.ValidUntilUtc is not null && latestObservation.ValidUntilUtc <= DateTimeOffset.UtcNow;
                var status = isStale
                    ? "Stale"
                    : ToUsageStatus(latestObservation.Status, lastCheck?.Status);
                observedAt = observedAt > latestObservation.ObservedAtUtc ? observedAt : latestObservation.ObservedAtUtc;
                snapshots.Add(new AccountUsageSnapshot(
                    accountId,
                    connection.ConnectionId,
                    connection.ProviderId,
                    group.Key.Capability,
                    ResolveScope(connection.ProviderId, connection.ConnectionKind, group.Key.Scope.ToString(), connection.Scope),
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
                    percentageFailureCode
                        ?? remainingPercentageFailureCode
                        ?? (lastCheck is { Status: not ProviderOperationStatus.Success and not ProviderOperationStatus.PartialSuccess }
                            ? lastCheck.SafeErrorCode
                            : null),
                    isStale,
                    ProductId: ResolveProductId(connection.ProviderId),
                    WindowType: ParseWindowType(ParseWindowDurationSeconds(values)),
                    WindowStartedAtUtc: windowStartedAt,
                    UsedPercentage: usedPercentage,
                    RemainingPercentage: remainingPercentage,
                    VerifiedAccountIdentity: providerIdentity,
                    IdentityBindingStatus: bindingStatus,
                    WindowDurationSeconds: ParseWindowDurationSeconds(values),
                    PlanType: ParsePlanType(values)));
            }

            if (usageGroups.Length == 0)
            {
                var descriptor = _registry.Descriptors.FirstOrDefault(item => item.ProviderId.Equals(connection.ProviderId, StringComparison.OrdinalIgnoreCase));
                var isChatGptWeb = connection.ProviderId.Equals("openai-chatgpt", StringComparison.OrdinalIgnoreCase)
                    && connection.ConnectionKind == ConnectionKind.Manual;
                if (isChatGptWeb)
                {
                    if (!TryAddChatGptWebUsage(accountId, connection, chatGptObservation, sync?.NextEligibleAtUtc, snapshots))
                    {
                        snapshots.Add(Unsupported(accountId, connection, sync?.NextEligibleAtUtc, "Chưa đọc được hạn mức từ phiên ChatGPT đã đăng nhập"));
                    }
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
                            false,
                            "Supported",
                            ProductId: ResolveProductId(connection.ProviderId),
                            IdentityBindingStatus: ResolveIdentityBinding(connection, connection.VerifiedIdentity)));
                    }
                }
            }
        }

        return new AccountUsageView(accountId, snapshots.OrderBy(item => item.ProviderId).ThenBy(item => item.Capability).ToArray(), observedAt);
    }

    private static bool TryAddChatGptWebUsage(
        Guid accountId,
        AccountConnectionSummary connection,
        ProviderAccountObservationSnapshot? observation,
        DateTimeOffset? nextCheckAtUtc,
        ICollection<AccountUsageSnapshot> snapshots)
    {
        if (observation is null
            || observation.AccountId != accountId
            || !observation.ProviderId.Equals(connection.ProviderId, StringComparison.OrdinalIgnoreCase)
            || !observation.AuthenticatedShellDetected)
        {
            return false;
        }

        var fields = observation.Fields;
        var observedEmail = fields.FirstOrDefault(item => item.Field == ProviderAccountObservationField.Email);
        var usage = fields.FirstOrDefault(item => item.Field == ProviderAccountObservationField.UsageQuota);
        var emailMatchesConnection = observedEmail?.Status == ProviderAccountObservationFieldStatus.Observed
            && !string.IsNullOrWhiteSpace(connection.VerifiedIdentity)
            && string.Equals(observedEmail.Value, connection.VerifiedIdentity, StringComparison.OrdinalIgnoreCase);
        var usageSnapshotHasVerifiedBinding = observedEmail?.Status == ProviderAccountObservationFieldStatus.Observed
            && observedEmail.Value is null
            && observedEmail.EvidenceType == "VerifiedChatGptUsageBindingV1"
            && observedEmail.Confidence == "Đã khớp account";
        var hasOfficialSettingsIdentity = observedEmail?.Status == ProviderAccountObservationFieldStatus.Observed
            && observedEmail.EvidenceType.Equals("OfficialSettingsAccount", StringComparison.Ordinal)
            && observedEmail.Source.Equals("OfficialSettingsAccount", StringComparison.Ordinal);
        if ((!observation.AccountMenuDetected && !hasOfficialSettingsIdentity && !usageSnapshotHasVerifiedBinding)
            || !(emailMatchesConnection || usageSnapshotHasVerifiedBinding)
            || usage?.Status != ProviderAccountObservationFieldStatus.Observed
            || !string.Equals(usage.EvidenceType, "ChatGptWebUsageV1", StringComparison.Ordinal)
            || !ChatGptWebUsageSnapshotCodec.TryDeserialize(usage.Value, out var apiSnapshot)
            || apiSnapshot is null)
        {
            return false;
        }

        var stale = apiSnapshot.ObservedAtUtc < DateTimeOffset.UtcNow.AddMinutes(-10);
        var planType = apiSnapshot.PlanType;
        var windowCount = 0;
        AddWindow("PrimaryWindow", apiSnapshot.PrimaryWindow);
        AddWindow("SecondaryWindow", apiSnapshot.SecondaryWindow);

        if (windowCount == 0 && planType is not null)
        {
            snapshots.Add(new AccountUsageSnapshot(
                accountId,
                connection.ConnectionId,
                connection.ProviderId,
                ProviderCapability.Quota,
                "ChatGPTWebPersonal",
                stale ? "Stale" : "Available",
                null,
                null,
                null,
                null,
                null,
                null,
                apiSnapshot.ObservedAtUtc,
                nextCheckAtUtc,
                usage.Source,
                null,
                stale,
                ProductId: "ChatGPTWeb",
                VerifiedAccountIdentity: connection.VerifiedIdentity,
                IdentityBindingStatus: UsageIdentityBindingStatus.ProviderIdentityVerified,
                PlanType: planType));
        }

        return windowCount > 0 || planType is not null;

        void AddWindow(string windowName, ChatGptUsageWindowSnapshot? window)
        {
            if (window is null)
            {
                return;
            }

            windowCount++;
            var usedPercent = window.UsedPercent;
            var remainingPercent = usedPercent is decimal value ? Math.Clamp(100m - value, 0m, 100m) : (decimal?)null;
            var resetAt = window.ResetAtUtc
                ?? (window.ResetAfterSeconds is long resetAfterSeconds
                    ? apiSnapshot.ObservedAtUtc.AddSeconds(resetAfterSeconds)
                    : null);
            snapshots.Add(new AccountUsageSnapshot(
                accountId,
                connection.ConnectionId,
                connection.ProviderId,
                ProviderCapability.Quota,
                $"ChatGPTWebPersonal.{windowName}",
                stale ? "Stale" : "Available",
                usedPercent,
                usedPercent is null ? null : 100m,
                remainingPercent,
                usedPercent,
                usedPercent is null ? null : "percent",
                resetAt,
                apiSnapshot.ObservedAtUtc,
                nextCheckAtUtc,
                usage.Source,
                null,
                stale,
                ProductId: "ChatGPTWeb",
                WindowType: ParseWindowType(window.LimitWindowSeconds),
                UsedPercentage: usedPercent,
                RemainingPercentage: remainingPercent,
                VerifiedAccountIdentity: connection.VerifiedIdentity,
                IdentityBindingStatus: UsageIdentityBindingStatus.ProviderIdentityVerified,
                PlanType: planType,
                WindowDurationSeconds: window.LimitWindowSeconds));
        }
    }

    private static AccountUsageWindowType ParseWindowType(long? limitWindowSeconds)
        => limitWindowSeconds switch
        {
            18_000 => AccountUsageWindowType.FiveHours,
            604_800 => AccountUsageWindowType.Week,
            > 0 => AccountUsageWindowType.Other,
            _ => AccountUsageWindowType.Unknown
        };

    private static long? ParseWindowDurationSeconds(IReadOnlyList<ProviderObservationSnapshot> observations)
    {
        var raw = observations.FirstOrDefault(item =>
            item.MetricName.Equals("limit_window_seconds", StringComparison.OrdinalIgnoreCase))?.Value;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            && seconds is > 0 and <= 31_536_000
                ? seconds
                : null;
    }

    private static DateTimeOffset? ParseResetAfter(
        IReadOnlyList<ProviderObservationSnapshot> observations,
        DateTimeOffset observedAtUtc)
    {
        var raw = observations.FirstOrDefault(item =>
            item.MetricName.Equals("reset_after_seconds", StringComparison.OrdinalIgnoreCase))?.Value;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            && seconds is > 0 and <= 31_536_000
                ? observedAtUtc.AddSeconds(seconds)
                : null;
    }

    private static string? ParsePlanType(IReadOnlyList<ProviderObservationSnapshot> observations)
    {
        var plan = observations.FirstOrDefault(item =>
            item.MetricName.Equals("plan_type", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
        return !string.IsNullOrWhiteSpace(plan)
            && plan.Length <= 64
            && plan.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
                ? plan.ToLowerInvariant()
                : null;
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
            "ChatGPTWebPersonal",
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
            false,
            "Unsupported",
            ProductId: ResolveProductId(connection.ProviderId),
            IdentityBindingStatus: UsageIdentityBindingStatus.Unavailable);

    private static string ResolveScope(
        string providerId,
        ConnectionKind connectionKind,
        string observedScope,
        string? configuredScope)
    {
        if (providerId.Equals("openai-chatgpt", StringComparison.OrdinalIgnoreCase)
            && connectionKind == ConnectionKind.Manual)
        {
            return "ChatGPTWebPersonal";
        }

        if (providerId.Equals("openai-platform", StringComparison.OrdinalIgnoreCase)
            || providerId.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            return (configuredScope ?? observedScope).Contains("project", StringComparison.OrdinalIgnoreCase)
                ? "OpenAIApiProject"
                : "OpenAIApiOrganization";
        }

        return observedScope;
    }

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

    private static decimal? CalculatePercentage(
        IReadOnlyList<ProviderObservationSnapshot> observations,
        decimal? numerator,
        decimal? denominator,
        out string? failureCode,
        params string[] numeratorMetricNames)
    {
        failureCode = null;
        if (numerator is null || denominator is not > 0)
        {
            return null;
        }

        var numeratorObservation = observations.FirstOrDefault(item => numeratorMetricNames.Length == 0
            ? item.MetricName.Equals("usage", StringComparison.OrdinalIgnoreCase)
                || item.MetricName.Equals("total_usage", StringComparison.OrdinalIgnoreCase)
                || item.MetricName.Equals("used", StringComparison.OrdinalIgnoreCase)
            : numeratorMetricNames.Any(name => item.MetricName.Equals(name, StringComparison.OrdinalIgnoreCase)));
        var denominatorObservation = observations.FirstOrDefault(item =>
            item.MetricName.Equals("limit", StringComparison.OrdinalIgnoreCase)
            || item.MetricName.Equals("quota_limit", StringComparison.OrdinalIgnoreCase)
            || item.MetricName.Equals("total_credits", StringComparison.OrdinalIgnoreCase));

        if (numeratorObservation is null
            || denominatorObservation is null
            || !UnitsMatch(numeratorObservation.Unit, denominatorObservation.Unit))
        {
            failureCode = "USAGE_PERCENTAGE_UNAVAILABLE_UNIT_MISMATCH";
            return null;
        }

        return Math.Clamp(numerator.Value / denominator.Value * 100m, 0m, 100m);
    }

    private static decimal? ParsePercentage(
        IReadOnlyList<ProviderObservationSnapshot> observations,
        string metricName)
    {
        var raw = observations.FirstOrDefault(item =>
            item.MetricName.Equals(metricName, StringComparison.OrdinalIgnoreCase))?.Value;
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage)
            && percentage is >= 0 and <= 100
                ? percentage
                : null;
    }

    private static bool UnitsMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        static string Normalize(string value)
            => value.Trim().ToLowerInvariant() switch
            {
                "request" => "requests",
                "token" => "tokens",
                "credit" => "credits",
                _ => value.Trim().ToLowerInvariant()
            };

        return Normalize(left) == Normalize(right);
    }

    private static string ResolveProductId(string providerId)
        => providerId.ToLowerInvariant() switch
        {
            "codex-cli" or "openai-codex-cli" => "CodexCLI",
            "openai-chatgpt" => "ChatGPTWeb",
            "openai-platform" or "openai" => "OpenAIApi",
            _ => providerId
        };

    private static string? ResolveProviderIdentity(
        AccountConnectionSummary connection,
        IReadOnlyList<ProviderObservationSnapshot> observations)
    {
        if (!string.IsNullOrWhiteSpace(connection.VerifiedIdentity))
        {
            return connection.VerifiedIdentity;
        }

        return observations
            .Where(item => item.Capability == ProviderCapability.Identity
                && item.Status is ProviderObservationStatus.Confirmed or ProviderObservationStatus.Derived)
            .Where(item => item.MetricName.Equals("subject", StringComparison.OrdinalIgnoreCase)
                || item.MetricName.Equals("provider_subject", StringComparison.OrdinalIgnoreCase)
                || item.MetricName.Equals("identity", StringComparison.OrdinalIgnoreCase)
                || item.MetricName.Equals("email", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static UsageIdentityBindingStatus ResolveIdentityBinding(
        AccountConnectionSummary connection,
        string? providerIdentity)
        => !string.IsNullOrWhiteSpace(connection.VerifiedIdentity)
            ? UsageIdentityBindingStatus.ProviderIdentityVerified
            : !string.IsNullOrWhiteSpace(providerIdentity)
                ? UsageIdentityBindingStatus.ProviderIdentityVerified
                : connection.ConnectionKind != ConnectionKind.Manual
                    && connection.CredentialAvailability == ConnectionCredentialAvailability.StoredReference
                    ? UsageIdentityBindingStatus.ConnectionScoped
                    : UsageIdentityBindingStatus.Unavailable;

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
