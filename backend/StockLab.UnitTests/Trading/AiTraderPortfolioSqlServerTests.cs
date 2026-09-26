using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiTraderPortfolioSqlServerTests
{
    [LocalDbFact]
    public async Task Concurrent_creators_return_one_portfolio_and_stale_cash_update_is_rejected()
    {
        // Hard-coded LocalDB only: never use the application's configured Azure connection.
        var database = $"StockLabAiPortfolioTests_{Guid.NewGuid():N}";
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).Options;
        await using var context = new StockLabDbContext(options);
        try
        {
            await context.Database.MigrateAsync();
            var gate = new CreationGate(8);
            var factory = new ContextFactory(new DbContextOptionsBuilder<StockLabDbContext>()
                .UseSqlServer(connection).AddInterceptors(gate).Options);
            var service = new AiTraderPortfolioService(factory, new NoQuotes(), TimeProvider.System);
            var states = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetOrCreateAsync()));
            Assert.Single(states.Select(s => s.PortfolioId).Distinct());
            Assert.Equal(1, await context.AiTraderPortfolios.CountAsync());
            Assert.All(states, state => Assert.Equal(100000m, state.CashBalance));
            Assert.Empty(await context.Users.ToListAsync());

            await using var stale = new StockLabDbContext(options);
            var stalePortfolio = await stale.AiTraderPortfolios.SingleAsync();
            var portfolio = await context.AiTraderPortfolios.SingleAsync();
            Assert.Equal(8, portfolio.Version.Length);
            var originalVersion = portfolio.Version.ToArray();
            portfolio.CashBalance = 74000m;
            await context.SaveChangesAsync();
            Assert.False(originalVersion.SequenceEqual(portfolio.Version));
            stalePortfolio.CashBalance = 90000m;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            Assert.Equal(74000m, (await service.GetOrCreateAsync()).CashBalance);
        }
        finally
        {
            // Only the randomly named database created by this test is removed.
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class ContextFactory(DbContextOptions<StockLabDbContext> options) : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new(options);
    }

    private sealed class CreationGate(int count) : SaveChangesInterceptor
    {
        private int remaining = count;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref remaining) == 0) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private sealed class NoQuotes : IMarketDataProvider
    {
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB to run the real concurrency check.";
        }
    }
}
