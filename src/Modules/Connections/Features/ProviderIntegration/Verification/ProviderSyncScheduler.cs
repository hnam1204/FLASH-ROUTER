using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

public sealed class ProviderSyncOptions
{
    public bool Enabled { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
}

public sealed class ProviderSyncScheduler : BackgroundService
{
    private readonly IProviderSyncOrchestrator _orchestrator;
    private readonly ProviderSyncOptions _options;
    private readonly ILogger<ProviderSyncScheduler> _logger;

    public ProviderSyncScheduler(
        IProviderSyncOrchestrator orchestrator,
        ProviderSyncOptions options,
        ILogger<ProviderSyncScheduler> logger)
    {
        _orchestrator = orchestrator;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        using var timer = new PeriodicTimer(_options.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _orchestrator.CheckAllAsync(new CheckAllRequest(Confirmed: true), cancellationToken: stoppingToken);
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
}
