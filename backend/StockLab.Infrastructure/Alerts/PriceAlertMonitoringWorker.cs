using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Alerts;

public sealed class PriceAlertMonitoringWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PriceAlertMonitoringOptions> options,
    TimeProvider timeProvider,
    ILogger<PriceAlertMonitoringWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var monitor = scope.ServiceProvider.GetRequiredService<IPriceAlertMonitoringService>();
                    await monitor.RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    logger.LogWarning("Price alert monitoring cycle failed; retrying at the next interval.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown interrupts both the timer wait and any current cycle.
        }
    }
}
