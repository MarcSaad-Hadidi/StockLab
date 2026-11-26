using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StockLab.Infrastructure.Identity;

public sealed class PasswordRecoveryWorker(PasswordRecoveryRequestQueue queue, IServiceScopeFactory scopes,
    TimeProvider timeProvider, ILogger<PasswordRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20), timeProvider);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PasswordRecoveryRequestProcessor>().ProcessAsync(request, linked.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // SMTP/provider exception details may contain addresses, credentials or the message body.
                logger.LogWarning("A password recovery request could not be delivered.");
            }
        }
    }
}
