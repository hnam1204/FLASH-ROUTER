using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

public sealed class ProviderSyncOptions
{
    public bool Enabled { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
    public bool AllowOfficialUsageReads { get; set; }
    public TimeSpan MaximumBackoff { get; set; } = TimeSpan.FromMinutes(30);
}

public sealed class ProviderSyncScheduler : BackgroundService
{
    private readonly IProviderSyncOrchestrator _orchestrator;
    private readonly IProviderSyncStateStore _syncStates;
    private readonly ProviderSyncOptions _options;
    private readonly ILogger<ProviderSyncScheduler> _logger;
    private readonly IAccountBrowserSessionManager? _browserSessions;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ConcurrentDictionary<Guid, BackoffState> _backoff = new();

    public ProviderSyncScheduler(
        IProviderSyncOrchestrator orchestrator,
        IProviderSyncStateStore syncStates,
        ProviderSyncOptions options,
        ILogger<ProviderSyncScheduler> logger,
        IAccountBrowserSessionManager? browserSessions = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _orchestrator = orchestrator;
        _syncStates = syncStates;
        _options = options;
        _logger = logger;
        _browserSessions = browserSessions;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        var interval = EffectiveInterval();
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Provider synchronization cycle failed safely.");
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        if (_browserSessions is not null)
        {
            await _browserSessions.ReconcileAsync(cancellationToken);
        }

        var preview = await _orchestrator.PreviewCheckAllAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var connection in preview.EligibleConnections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var persisted = await _syncStates.GetAsync(connection.ConnectionId, cancellationToken);
            if (persisted is null || !persisted.Enabled)
            {
                continue;
            }

            var state = _backoff.TryGetValue(connection.ConnectionId, out var memoryState)
                ? memoryState
                : null;
            var nextEligibleAtUtc = persisted.NextEligibleAtUtc ?? state?.NextEligibleAtUtc;
            if (nextEligibleAtUtc > now)
            {
                continue;
            }

            preview.CapabilitiesByProvider.TryGetValue(connection.ProviderId, out var availableCapabilities);
            var capabilities = (availableCapabilities ?? new HashSet<ProviderCapability>())
                .Where(capability => _options.AllowOfficialUsageReads
                    ? true
                    : capability is not ProviderCapability.Usage
                        and not ProviderCapability.TokenUsage
                        and not ProviderCapability.Costs)
                .ToHashSet();

            if (capabilities.Count == 0)
            {
                await AuditAsync(connection.AccountId, "UsageCheckUnsupported", "Không có capability usage được cho phép hoặc đã được provider công bố.", "Info", connection.ProviderId, "UNSUPPORTED");
                var next = now.Add(EffectiveInterval());
                _backoff[connection.ConnectionId] = new BackoffState(next, 0);
                await _syncStates.UpdateAsync(connection.ConnectionId, next, persisted.LastSuccessfulSyncAtUtc, 0, cancellationToken);
                continue;
            }

            try
            {
                await AuditAsync(connection.AccountId, "UsageCheckScheduled", "Đã lên lịch kiểm tra capability được provider cho phép.", "Info", connection.ProviderId, "scheduled");
                await AuditAsync(connection.AccountId, "UsageCheckStarted", "Đã bắt đầu kiểm tra usage theo credential API đã cấp quyền.", "Info", connection.ProviderId, "started");
                var result = await _orchestrator.CheckConnectionAsync(
                    connection.ConnectionId,
                    capabilities,
                    allowBillableRead: _options.AllowOfficialUsageReads,
                    cancellationToken);
                await AuditAsync(connection.AccountId, "UsageCheckCompleted", "Kiểm tra provider đã hoàn tất; dữ liệu không được suy diễn khi thiếu nguồn.", result.Status is ProviderOperationStatus.Success or ProviderOperationStatus.PartialSuccess ? "Success" : "Warning", connection.ProviderId, result.Status.ToString());
                if (result.Observations.Count > 0)
                {
                    await AuditAsync(connection.AccountId, "UsageSnapshotPersisted", "Đã lưu các quan sát provider có nguồn chính thức.", "Success", connection.ProviderId, "persisted");
                }
                if (result.Status == ProviderOperationStatus.RateLimited)
                {
                    await AuditAsync(connection.AccountId, "UsageCheckRateLimited", "Provider đã giới hạn yêu cầu; scheduler áp dụng backoff.", "Warning", connection.ProviderId, "RATE_LIMITED");
                }
                if (IsTransient(result.Status))
                {
                    var failures = Math.Max(persisted.FailureCount, state?.FailureCount ?? 0) + 1;
                    var delay = TimeSpan.FromSeconds(Math.Min(
                        _options.MaximumBackoff.TotalSeconds,
                        Math.Pow(2, Math.Min(failures, 8)) * 60));
                    var next = now.Add(delay);
                    _backoff[connection.ConnectionId] = new BackoffState(next, failures);
                    await _syncStates.UpdateAsync(connection.ConnectionId, next, persisted.LastSuccessfulSyncAtUtc, failures, cancellationToken);
                }
                else
                {
                    var next = now.Add(EffectiveInterval());
                    var lastSuccess = result.Status is ProviderOperationStatus.Success or ProviderOperationStatus.PartialSuccess
                        ? now
                        : persisted.LastSuccessfulSyncAtUtc;
                    _backoff[connection.ConnectionId] = new BackoffState(next, 0);
                    await _syncStates.UpdateAsync(connection.ConnectionId, next, lastSuccess, 0, cancellationToken);
                }
            }
            catch (Exception exception)
            {
                await AuditAsync(connection.AccountId, "UsageCheckFailed", "Kiểm tra usage thất bại và đã được backoff an toàn.", "Warning", connection.ProviderId, "SYNC_FAILED");
                var failures = Math.Max(persisted.FailureCount, state?.FailureCount ?? 0) + 1;
                var delay = TimeSpan.FromSeconds(Math.Min(
                    _options.MaximumBackoff.TotalSeconds,
                    Math.Pow(2, Math.Min(failures, 8)) * 60));
                var next = now.Add(delay);
                _backoff[connection.ConnectionId] = new BackoffState(next, failures);
                await _syncStates.UpdateAsync(connection.ConnectionId, next, persisted.LastSuccessfulSyncAtUtc, failures, cancellationToken);
                _logger.LogWarning(exception, "Provider monitoring cycle failed for connection {ConnectionId}; retry is backed off.", connection.ConnectionId);
            }
        }
    }

    private async Task AuditAsync(Guid accountId, string eventType, string summary, string severity, string providerId, string outcome)
    {
        if (_scopeFactory is null)
        {
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sink = scope.ServiceProvider.GetService<IAuditSink>();
            if (sink is not null)
            {
                await sink.WriteAsync(accountId, eventType, summary, severity, providerId: providerId, source: "ProviderSyncScheduler", outcome: outcome);
            }
        }
        catch
        {
            // Usage logging must not prevent provider backoff or shutdown.
        }
    }

    private static bool IsTransient(ProviderOperationStatus status)
        => status is ProviderOperationStatus.RateLimited
            or ProviderOperationStatus.Timeout
            or ProviderOperationStatus.NetworkFailure
            or ProviderOperationStatus.ProviderUnavailable;

    private TimeSpan EffectiveInterval()
        => _options.Interval > TimeSpan.Zero ? _options.Interval : TimeSpan.FromSeconds(60);

    private sealed record BackoffState(DateTimeOffset NextEligibleAtUtc, int FailureCount);
}
