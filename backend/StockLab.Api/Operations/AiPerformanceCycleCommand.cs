using StockLab.Application.Interfaces;

namespace StockLab.Api.Operations;

public static class AiPerformanceCycleCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, TextWriter output, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IAiPerformanceCycleService>().RunOnceAsync(cancellationToken);
            await output.WriteLineAsync($"performance_cycle: capture={result.Cycle.CaptureId:D}; slot={result.Cycle.CycleSlotUtc:O}; replay={result.IsReplay}");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("performance_cycle_cancelled: retry the current slot to recover its recorded result.");
            return 1;
        }
        catch (Exception)
        {
            await output.WriteLineAsync("performance_cycle_failed: inspect target, migrations, portfolio and valuation configuration.");
            return 1;
        }
    }
}
