using System.Globalization;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Api.Operations;

/// <summary>One trusted operator cycle; no HTTP host, background worker, risk or order execution.</summary>
public static class AiDecisionPipelineCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration,
        TextWriter output, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DateOnly.TryParseExact(configuration["AiDecisionPipeline:DecisionDate"], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var session))
            {
                await output.WriteLineAsync("decision_date_required: supply a closed session in YYYY-MM-DD format.");
                return 1;
            }
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var database = provider.GetRequiredService<StockLabDbContext>();
            if (string.IsNullOrWhiteSpace(configuration["AiDecisionPipeline:ExpectedDatabase"])
                || !string.Equals(configuration["AiDecisionPipeline:ExpectedDatabase"],
                    database.Database.GetDbConnection().Database, StringComparison.Ordinal))
            {
                await output.WriteLineAsync("database_target_mismatch: supply the exact expected database name.");
                return 1;
            }
            if (!await database.Database.CanConnectAsync(cancellationToken))
            {
                await output.WriteLineAsync("database_unavailable");
                return 1;
            }
            if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                await output.WriteLineAsync("migrations_pending: apply migrations explicitly before inference.");
                return 1;
            }
            var records = await provider.GetRequiredService<IAiDecisionPipelineService>().RunAsync(session, cancellationToken);
            await output.WriteLineAsync($"decisions_recorded: {records.Count}");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("decision_pipeline_cancelled");
            return 1;
        }
        catch (Exception)
        {
            await output.WriteLineAsync("decision_pipeline_failed: check private configuration and existing history before retrying.");
            return 1;
        }
    }
}
