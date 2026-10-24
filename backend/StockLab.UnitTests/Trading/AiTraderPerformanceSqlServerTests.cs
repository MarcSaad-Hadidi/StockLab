using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using static StockLab.UnitTests.Trading.AiTraderPerformanceTests;

namespace StockLab.UnitTests.Trading;

[Trait("Category", "SqlServer")]
public sealed class AiTraderPerformanceSqlServerTests
{
    [LocalDbFact]
    public async Task Sql_smoke_executes_real_trades_and_reads_metrics_and_valuations()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await VerifySmokeAsync(f);
    }

    [LocalDbFact]
    public async Task Sql_migration_preserves_legacy_trades_cash_and_positions_through_upgrade_and_down()
    {
        const string previous = "20261005015352_PreservePaperTradingAmountPrecision";
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabPerformanceUpgrade_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
        await using var f = new AiCurrentPositionsFixture(connection, null);
        await using var db = f.CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        var p = new AiTraderPortfolio { Id = Guid.NewGuid(), CashBalance = 99000m,
            CreatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime, UpdatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime };
        db.AiTraderPortfolios.Add(p);
        db.AiTraderPositions.Add(new() { Id = Guid.NewGuid(), AiTraderPortfolioId = p.Id, Symbol = "AAPL",
            Quantity = 10m, AverageCost = 100m, CreatedAtUtc = p.CreatedAtUtc, UpdatedAtUtc = p.UpdatedAtUtc });
        await db.SaveChangesAsync();
        var id = Guid.NewGuid();
        var order = Guid.NewGuid();
        var fingerprint = new string('A', 64);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AiTrades (Id,AiTraderPortfolioId,OrderId,OrderFingerprint,Side,Symbol,Quantity,ExecutionPrice,TotalAmount,ExecutedAtUtc,CashBalanceAfter,PositionQuantityAfter,AverageCostAfter) VALUES ({id},{p.Id},{order},{fingerprint},'SELL','AAPL',1,100,100,{p.CreatedAtUtc},99000,10,100)");
        var version = p.Version.ToArray();
        await db.Database.MigrateAsync();
        var trade = await db.AiTrades.AsNoTracking().SingleAsync();
        Assert.Equal(id, trade.Id);
        Assert.Null(trade.RealizedPnL);
        Assert.Empty(await db.AiTraderValuationSnapshots.ToArrayAsync());
        Assert.False((await Metrics(f).GetMetricsAsync()).TradeStatisticsComplete);
        Assert.Equal(10m, (await db.AiTraderPositions.SingleAsync()).Quantity);
        Assert.Equal(99000m, (await db.AiTraderPortfolios.AsNoTracking().SingleAsync()).CashBalance);
        Assert.Equal(version, (await db.AiTraderPortfolios.AsNoTracking().SingleAsync()).Version);
        await db.GetService<IMigrator>().MigrateAsync(previous);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM AiTrades").SingleAsync());
        Assert.Equal(1, await db.AiTraderPositions.CountAsync());
        Assert.Equal(99000m, (await db.AiTraderPortfolios.AsNoTracking().SingleAsync()).CashBalance);
        await db.Database.MigrateAsync();
        Assert.Null((await db.AiTrades.AsNoTracking().SingleAsync()).RealizedPnL);
    }

    [LocalDbFact]
    public async Task Sql_capture_race_commits_one_observation_and_identical_retry_replays_it()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var market = new GatedMarket(f.ValuationMarket);
        var portfolio = new AiTraderPortfolioService(f, market, new AiCurrentPositionsFixture.Clock());
        var service = new AiTraderValuationHistoryService(f, portfolio);
        var id = Guid.NewGuid();
        async Task<object> Capture()
        {
            try { return await service.CaptureCurrentAsync(id); }
            catch (AiTraderPerformanceException ex) { return ex; }
        }
        var results = await Task.WhenAll(Capture(), Capture()).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.NotEmpty(results.OfType<AiTraderEquityObservation>());
        Assert.All(results.OfType<AiTraderPerformanceException>(), e => Assert.Equal(AiTraderPerformanceFailure.ConcurrencyConflict, e.Category));
        var replay = await History(f).CaptureCurrentAsync(id);
        Assert.All(results.OfType<AiTraderEquityObservation>(), p => Assert.Equal(replay.Id, p.Id));
        await using var db = f.CreateDbContext();
        Assert.Equal(2, await db.AiTraderValuationSnapshots.CountAsync());
    }

    [LocalDbFact]
    public async Task Sql_rowversion_change_during_quote_prevents_capture_and_metrics_publication()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync();
        foreach (var capture in new[] { false, true })
        {
            f.ValuationMarket.Quote = (symbol, _) =>
            {
                using var writer = f.CreateDbContext();
                var p = writer.AiTraderPortfolios.Single();
                // Same values and timestamp still change SQL Server's rowversion.
                writer.Entry(p).Property(p => p.UpdatedAtUtc).IsModified = true;
                writer.SaveChanges();
                return new(symbol, "USD", 120m, null, null, null, AiCurrentPositionsFixture.Now);
            };
            var error = await Assert.ThrowsAsync<AiTraderPerformanceException>(() => capture
                ? (Task)History(f).CaptureCurrentAsync(Guid.NewGuid()) : Metrics(f).GetMetricsAsync());
            Assert.Equal(AiTraderPerformanceFailure.ConcurrencyConflict, error.Category);
        }
        Assert.Single(await History(f).GetHistoryAsync());
    }

    [LocalDbFact]
    public async Task Sql_failed_capture_rolls_back_and_does_not_hold_locks_during_quotes()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var observer = new TransactionObserver();
        f.ValuationMarket.Quote = (symbol, _) =>
        {
            Assert.False(observer.Started);
            return new(symbol, "USD", 120m, null, null, null, AiCurrentPositionsFixture.Now);
        };
        var factory = f.With(observer);
        var service = new AiTraderValuationHistoryService(factory, Portfolio(f));
        var before = await f.StateAsync();
        var error = await Assert.ThrowsAsync<AiTraderPerformanceException>(() => service.CaptureCurrentAsync(Guid.NewGuid()));
        Assert.Equal(AiTraderPerformanceFailure.PersistenceFailure, error.Category);
        Assert.True(observer.Started);
        Assert.Equal(before, await f.StateAsync());
    }

    [LocalDbFact]
    public async Task Sql_snapshot_constraints_and_unique_capture_protect_historical_facts()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        var portfolio = await f.InitializeAsync();
        var first = await History(f).CaptureCurrentAsync(Guid.NewGuid());
        await using var db = f.CreateDbContext();
        foreach (var failure in new[] { "cash", "positions", "currency", "timestamp", "capture", "duplicate", "fk" })
        {
            db.AiTraderValuationSnapshots.Add(new()
            {
                Id = Guid.NewGuid(), AiTraderPortfolioId = failure == "fk" ? Guid.NewGuid() : portfolio.PortfolioId,
                CaptureId = failure == "capture" ? Guid.Empty : failure == "duplicate" ? first.CaptureId : Guid.NewGuid(),
                ValuedAtUtc = failure == "timestamp" ? default : AiCurrentPositionsFixture.Now.UtcDateTime,
                CashBalance = failure == "cash" ? -1m : 100000m, PositionsValue = failure == "positions" ? -1m : 0m,
                Currency = failure == "currency" ? "CAD" : "USD"
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 547, 2601, 2627 });
            db.ChangeTracker.Clear();
        }
        var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AiPortfolios WHERE Id = {portfolio.PortfolioId}"));
        Assert.Equal(547, delete.Number);
        Assert.Equal(2, await db.AiTraderValuationSnapshots.CountAsync());
    }

    [LocalDbFact]
    public async Task Sql_failed_sell_rolls_back_realized_pnl_cash_and_position_together()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var request = await f.RecordExecutionAsync(quantity: 4m, price: 120m, signal: AiTradingSignal.Sell);
        var before = await f.StateAsync();
        var failure = await Assert.ThrowsAsync<AiTradeExecutionException>(() => f.ExecuteAsync(request, new TransactionObserver()));
        Assert.Equal(AiTradeExecutionFailure.PersistenceFailure, failure.Category);
        Assert.Equal(before, await f.StateAsync());
        var committed = await f.ExecuteAsync(request);
        await using var db = f.CreateDbContext();
        Assert.Equal(80m, (await db.AiTrades.SingleAsync(t => t.Id == committed.TradeId)).RealizedPnL);
        Assert.Equal(6m, (await db.AiTraderPositions.SingleAsync()).Quantity);
        Assert.Equal(99480m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [LocalDbFact]
    public async Task Sql_fractional_sale_and_valuation_preserve_four_decimal_settlement()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        await f.ExecuteAsync(quantity: 3.12500001m, price: 80.1234m);
        await f.ExecuteAsync(quantity: 1.12500001m, price: 100.5678m, signal: AiTradingSignal.Sell);
        f.ValuationMarket.Prices["AAPL"] = 120.123456m;
        var point = await History(f).CaptureCurrentAsync(Guid.NewGuid());
        Assert.Equal(240.2469m, point.PositionsValue);
        var m = await Metrics(f).GetMetricsAsync();
        Assert.Equal(23m, m.RealizedPnL);
        Assert.Equal(240.246912m, m.PositionsMarketValue);
        Assert.Equal(2m, Assert.Single(await f.Service().GetCurrentAsync()).Quantity);
    }

    private sealed class GatedMarket(IMarketDataProvider inner) : IMarketDataProvider
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return await inner.GetQuoteAsync(symbol, cancellationToken);
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TransactionObserver : DbTransactionInterceptor, ISaveChangesInterceptor
    {
        public bool Started { get; private set; }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        {
            Started = true;
            Assert.Equal(System.Data.IsolationLevel.Serializable, result.IsolationLevel);
            return ValueTask.FromResult(result);
        }
        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new DbUpdateException("private-storage-marker");
    }

    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
