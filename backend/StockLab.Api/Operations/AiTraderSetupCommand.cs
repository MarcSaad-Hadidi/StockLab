using Microsoft.EntityFrameworkCore;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Api.Operations;

/// <summary>Explicit operator commands; never invoked by a request or normal API startup.</summary>
public static class AiTraderSetupCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration,
        bool initialize, TextWriter output, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var database = provider.GetRequiredService<StockLabDbContext>();
            if (initialize && (string.IsNullOrWhiteSpace(configuration["AiTraderSetup:ExpectedDatabase"])
                || !string.Equals(configuration["AiTraderSetup:ExpectedDatabase"],
                    database.Database.GetDbConnection().Database, StringComparison.Ordinal)))
            {
                await output.WriteLineAsync("database_target_mismatch: supply AiTraderSetup:ExpectedDatabase matching the configured database.");
                return 1;
            }
            if (!await database.Database.CanConnectAsync(cancellationToken))
            {
                await output.WriteLineAsync("database_unavailable: check the configured connection.");
                return 1;
            }
            if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                await output.WriteLineAsync("migrations_pending: review and apply migrations explicitly before setup.");
                return 1;
            }

            var portfolio = provider.GetRequiredService<IAiTraderPortfolioService>();
            if (initialize)
            {
                var state = await portfolio.GetOrCreateAsync(cancellationToken);
                await output.WriteLineAsync($"portfolio_ready: {state.PortfolioId:D}; currency={state.Currency}; open_positions={state.Positions.Count}.");
                return 0;
            }

            var ready = true;
            try
            {
                await portfolio.GetStateAsync(cancellationToken, initializeIfMissing: false);
                await output.WriteLineAsync("portfolio_ready");
            }
            catch (InvalidOperationException)
            {
                ready = false;
                await output.WriteLineAsync("portfolio_not_initialized: run --initialize-ai-trader explicitly.");
            }
            ready &= await CheckCatalog("models", async () =>
                (await provider.GetRequiredService<IAiModelCatalogReadService>().GetAsync(cancellationToken)).Count, output);
            ready &= await CheckCatalog("backtests", async () =>
                (await provider.GetRequiredService<IAiBacktestCatalogReadService>().GetAsync(cancellationToken)).Count, output);
            return ready ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("setup_cancelled");
            return 1;
        }
        catch (Exception)
        {
            // Connection strings, SQL/provider errors and private source paths must not reach console output.
            await output.WriteLineAsync("setup_failed: check database access and backend configuration.");
            return 1;
        }
    }

    private static async Task<bool> CheckCatalog(string name, Func<Task<int>> read, TextWriter output)
    {
        try
        {
            var count = await read();
            await output.WriteLineAsync($"{name}_ready: {count} validated entries{(count == 0 ? " (valid empty catalog)" : "")}.");
            return count > 0;
        }
        catch (CatalogUnavailableException error)
        {
            await output.WriteLineAsync($"{name}_{(error.IsInvalid ? "invalid" : "unavailable")}: configure authentic ML exports using AiTraderMetadata.");
            return false;
        }
    }
}
