using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using static StockLab.UnitTests.Trading.AiPaperTradingEngineTests;

namespace StockLab.UnitTests.Trading;

public sealed class AiPaperTradingSqlServerTests
{
    [LocalDbTheory]
    [InlineData("same-order")]
    [InlineData("cash")]
    [InlineData("exposure")]
    [InlineData("positions")]
    [InlineData("sell")]
    public async Task Concurrent_executions_cannot_double_spend_exceed_risk_or_short(string scenario)
    {
        await using var f = await SqlFixture.CreateAsync();
        await using var db = f.CreateDbContext();
        var p = new AiTraderPortfolio { Id = Guid.NewGuid(), CreatedAtUtc = Fixture.Before, UpdatedAtUtc = Fixture.Before };
        if (scenario == "cash")
        {
            p.CashBalance = 100m;
            p.Positions.Add(Position(p.Id, "OTHER", 999m, 100m));
        }
        if (scenario == "sell") p.Positions.Add(Position(p.Id, "AAPL", 10m, 100m));
        db.AiTraderPortfolios.Add(p);
        await db.SaveChangesAsync();
        var originalVersion = p.Version.ToArray();
        var policy = new AiRiskOptions { MaxCashAllocationPerTradePercent = 0.2m };
        if (scenario == "positions") policy.MaxOpenPositions = 1;
        var first = Request(scenario == "sell" ? AiTradingSignal.Sell : AiTradingSignal.Buy, scenario == "sell" ? 10m : 200m);
        var second = scenario == "same-order" ? first : first with { OrderId = Guid.NewGuid(), DecisionId = Guid.NewGuid() };
        if (scenario is "cash" or "positions") second = second with { RiskDecision = second.RiskDecision with { Symbol = "MSFT" } };
        await f.RecordAsync(first);
        if (scenario != "same-order") await f.RecordAsync(second);
        var gate = new QuoteGate();
        async Task<object> Execute(AiTradeExecutionRequest request)
        {
            var market = new FakeMarket { BeforeQuote = gate.WaitAsync };
            try { return await new AiPaperTradingEngine(f, market, Options.Create(policy), new FixedClock()).ExecuteAsync(request); }
            catch (AiTradeExecutionException e) { return e; }
        }
        var results = await Task.WhenAll(Execute(first), Execute(second)).WaitAsync(TimeSpan.FromSeconds(60));
        var successes = results.OfType<AiTradeExecutionResult>().ToArray();
        Assert.NotEmpty(successes);
        Assert.All(results.OfType<AiTradeExecutionException>(), e => Assert.Equal(AiTradeExecutionFailure.ConcurrencyConflict, e.Category));
        db.ChangeTracker.Clear();
        var trade = await db.AiTrades.SingleAsync();
        Assert.All(successes, r => Assert.Equal(trade.Id, r.TradeId));
        p = await db.AiTraderPortfolios.Include(row => row.Positions).SingleAsync();
        Assert.False(originalVersion.SequenceEqual(p.Version));
        Assert.True(p.CashBalance >= 0m);
        Assert.True(p.Positions.Count <= policy.MaxOpenPositions);
        Assert.All(p.Positions, position => Assert.True(position.Quantity > 0m));
        if (scenario == "sell")
        {
            Assert.Empty(p.Positions);
            Assert.Equal(101000m, p.CashBalance);
        }
        else
        {
            var totalValue = p.CashBalance + p.Positions.Sum(position => position.Quantity * 100m);
            Assert.All(p.Positions.Where(position => position.Symbol != "OTHER"), position =>
                Assert.True(position.Quantity * 100m <= totalValue * policy.MaxPositionExposurePercent));
            Assert.Equal(scenario == "cash" ? 0m : 80000m, p.CashBalance);
        }
        // Same order is replayable even if the competing request returned a controlled conflict.
        var replay = await new AiPaperTradingEngine(f, new FakeMarket(), Options.Create(policy), new FixedClock()).ExecuteAsync(
            trade.OrderId == first.OrderId ? first : second);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal(trade.Id, replay.TradeId);
    }

    [LocalDbFact]
    public async Task Sql_failure_after_save_rolls_back_all_writes_and_no_market_call_holds_a_transaction()
    {
        await using var f = await SqlFixture.CreateAsync();
        await using var db = f.CreateDbContext();
        db.AiTraderPortfolios.Add(new() { Id = Guid.NewGuid(), CreatedAtUtc = Fixture.Before, UpdatedAtUtc = Fixture.Before });
        await db.SaveChangesAsync();
        var original = (await db.AiTraderPortfolios.SingleAsync()).Version.ToArray();
        var observer = new TransactionObserver();
        var factory = new ContextFactory(new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(f.Connection)
            .AddInterceptors(observer, new FailAfterSql()).Options);
        var market = new FakeMarket { BeforeQuote = () => { Assert.False(observer.Started); return Task.CompletedTask; } };
        var request = await f.RequestAsync();
        await Failure(new AiPaperTradingEngine(factory, market, Options.Create(new AiRiskOptions()), new FixedClock()),
            request, AiTradeExecutionFailure.PersistenceFailure);
        Assert.True(observer.Started);
        Assert.Equal(IsolationLevel.Serializable, observer.Isolation);
        db.ChangeTracker.Clear();
        var portfolio = await db.AiTraderPortfolios.SingleAsync();
        Assert.Equal(100000m, portfolio.CashBalance);
        Assert.Equal(Fixture.Before, portfolio.UpdatedAtUtc);
        Assert.Equal(original, portfolio.Version);
        Assert.Empty(await db.AiTraderPositions.ToListAsync());
        Assert.Empty(await db.AiTrades.ToListAsync());
    }

    [LocalDbFact]
    public async Task Buy_caps_quantity_at_remaining_schema_capacity()
    {
        await using var f = await SqlFixture.CreateAsync();
        await f.SeedAsync(1000000000000m, ("AAPL", 99999999998.99999999m, 1m));
        f.Market.Price = 1m;
        var result = await f.Engine().ExecuteAsync(await f.RequestAsync());
        Assert.Equal(1m, result.ExecutedQuantity);
        Assert.Equal(99999999999.99999999m, result.PositionQuantity);
        Assert.Equal(999999999999m, result.CashBalance);
    }

    [LocalDbTheory]
    [InlineData("position-full", AiTradeExecutionFailure.RiskChanged)]
    [InlineData("cash-overflow", AiTradeExecutionFailure.StorageCapacityExceeded)]
    [InlineData("total-overflow", AiTradeExecutionFailure.StorageCapacityExceeded)]
    [InlineData("quantity-too-small", AiTradeExecutionFailure.TradeTooSmall)]
    public async Task Storage_boundaries_fail_without_partial_updates(string scenario, AiTradeExecutionFailure failure)
    {
        await using var f = await SqlFixture.CreateAsync();
        var cash = scenario == "cash-overflow" ? 999999999999999.9999m : 1000000000000m;
        var held = scenario == "position-full" ? 99999999999.99999999m : 100m;
        await f.SeedAsync(cash, ("AAPL", held, 1m));
        f.Market.Price = scenario == "total-overflow" ? 100000000000000m : 1m;
        var request = await f.RequestAsync(scenario.EndsWith("overflow") ? AiTradingSignal.Sell : AiTradingSignal.Buy,
            scenario == "quantity-too-small" ? 0.000000001m : 100m);
        await Failure(f.Engine(), request, failure);
        await using var db = f.CreateDbContext();
        Assert.Equal(cash, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Equal(held, (await db.AiTraderPositions.SingleAsync()).Quantity);
        Assert.Empty(await db.AiTrades.ToListAsync());
    }

    private sealed class QuoteGate
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitAsync()
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
    private sealed class TransactionObserver : DbTransactionInterceptor
    {
        public bool Started { get; private set; }
        public IsolationLevel Isolation { get; private set; }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        {
            Started = true;
            Isolation = result.IsolationLevel;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailAfterSql : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Injected failure after SQL completed, before transaction commit");
    }
    private sealed class ContextFactory(DbContextOptions<StockLabDbContext> options) : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new(options);
    }
    private sealed class SqlFixture(string connection) : IDbContextFactory<StockLabDbContext>, IAsyncDisposable
    {
        public string Connection => connection;
        public FakeMarket Market { get; } = new();
        public AiPaperTradingEngine Engine() => new(this, Market, Options.Create(new AiRiskOptions()), new FixedClock());
        public async Task<AiTradeExecutionRequest> RequestAsync(AiTradingSignal signal = AiTradingSignal.Buy, decimal quantity = 100m)
        {
            var request = Request(signal, quantity);
            await RecordAsync(request);
            return request;
        }
        public Task<AiDecisionRecord> RecordAsync(AiTradeExecutionRequest request) =>
            new AiDecisionHistoryService(this, new FixedClock()).RecordAsync(new(request.DecisionId,
                request.RiskDecision.Symbol, request.RiskDecision.Signal, request.RiskDecision.Confidence,
                new(2026, 9, 27), "test-model", "v1"));
        public StockLabDbContext CreateDbContext() => new(new DbContextOptionsBuilder<StockLabDbContext>().UseSqlServer(connection).Options);
        public async Task SeedAsync(decimal cash, params (string Symbol, decimal Quantity, decimal Cost)[] positions)
        {
            await using var db = CreateDbContext();
            var portfolio = new AiTraderPortfolio { Id = Guid.NewGuid(), CashBalance = cash,
                CreatedAtUtc = Fixture.Before, UpdatedAtUtc = Fixture.Before };
            db.AiTraderPortfolios.Add(portfolio);
            db.AiTraderPositions.AddRange(positions.Select(p => Position(portfolio.Id, p.Symbol, p.Quantity, p.Cost)));
            await db.SaveChangesAsync();
        }
        public static async Task<SqlFixture> CreateAsync()
        {
            // Never use application credentials or Azure: each test owns a random LocalDB database.
            var f = new SqlFixture($"Server=(localdb)\\MSSQLLocalDB;Database=StockLabAiExecutionTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true");
            await using var db = f.CreateDbContext();
            await db.Database.MigrateAsync();
            return f;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
    private sealed class LocalDbTheoryAttribute : TheoryAttribute
    {
        public LocalDbTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
