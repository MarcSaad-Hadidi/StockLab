using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Api.Operations;

/// <summary>Explicit persisted IDs only; no inference, implicit setup, HTTP listener or real broker.</summary>
public static class AiRiskExecutionCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration,
        TextWriter output, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = configuration.GetSection("AiRiskExecution:DecisionIds").Get<string[]>() ?? [];
            if (values.Length is < 1 or > 100 || values.Any(value => !Guid.TryParse(value, out var id) || id == Guid.Empty))
            {
                await output.WriteLineAsync("decision_ids_required: supply 1..100 distinct persisted decision IDs.");
                return 1;
            }
            var ids = values.Select(Guid.Parse).ToArray();
            if (ids.Distinct().Count() != ids.Length)
            {
                await output.WriteLineAsync("duplicate_decision_ids");
                return 1;
            }
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var db = provider.GetRequiredService<StockLabDbContext>();
            if (string.IsNullOrWhiteSpace(configuration["AiRiskExecution:ExpectedDatabase"])
                || !string.Equals(configuration["AiRiskExecution:ExpectedDatabase"], db.Database.GetDbConnection().Database, StringComparison.Ordinal))
            {
                await output.WriteLineAsync("database_target_mismatch: supply the exact expected database name.");
                return 1;
            }
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                await output.WriteLineAsync("database_unavailable");
                return 1;
            }
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                await output.WriteLineAsync("migrations_pending: apply migrations explicitly before execution.");
                return 1;
            }
            if (await db.AiDecisions.CountAsync(d => ids.Contains(d.Id), cancellationToken) != ids.Length)
            {
                await output.WriteLineAsync("decision_not_found: the complete explicit ID set must exist in this database.");
                return 1;
            }
            try { await provider.GetRequiredService<IAiTraderPortfolioService>().GetStateAsync(cancellationToken, initializeIfMissing: false); }
            catch (InvalidOperationException)
            {
                await output.WriteLineAsync("portfolio_not_initialized: use explicit AI Trader setup first.");
                return 1;
            }
            var results = new List<AiRiskExecutionResult>(ids.Length);
            var execution = provider.GetRequiredService<IAiRiskExecutionService>();
            foreach (var id in ids)
            {
                var result = await execution.ExecuteAsync(id, cancellationToken);
                results.Add(result);
                await output.WriteLineAsync($"decision_outcome: {id:D}; status={result.Status}; replay={result.IsReplay}");
            }
            await output.WriteLineAsync($"risk_execution: executed={results.Count(r => r.Status == "EXECUTED")}; rejected={results.Count(r => r.Status == "REJECTED")}; hold={results.Count(r => r.Status == "HOLD")}; failed={results.Count(r => r.Status == "FAILED")}; replayed={results.Count(r => r.IsReplay)}");
            return results.Any(r => r.Status == "FAILED") ? 1 : 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("risk_execution_cancelled: inspect recorded attempts before retrying.");
            return 1;
        }
        catch (Exception)
        {
            await output.WriteLineAsync("risk_execution_failed: inspect recorded attempts and configuration before retrying.");
            return 1;
        }
    }
}
