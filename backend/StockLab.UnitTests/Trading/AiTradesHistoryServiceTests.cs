using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiTradesHistoryServiceTests
{
    [Fact]
    public async Task Executed_decision_cannot_later_be_recorded_as_rejected()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await VerifyRejectionAfterExecutionAsync(f);
    }

    internal static async Task VerifyRejectionAfterExecutionAsync(AiCurrentPositionsFixture f)
    {
        await f.InitializeAsync();
        var request = await f.RecordExecutionAsync();
        await f.ExecuteAsync(request);
        var before = await f.StateAsync();
        var rejected = new AiRejectedDecisionRecordRequest(request.DecisionId, request.RiskDecision with
            { Approved = false, ApprovedQuantity = 0m, RejectionReason = AiRiskRejectionReason.LowConfidence });
        var service = new AiRejectedDecisionHistoryService(f, new AiCurrentPositionsFixture.Clock());
        var failure = await Assert.ThrowsAsync<AiRejectedDecisionHistoryException>(() => service.RecordAsync(rejected));
        Assert.Equal(AiRejectedDecisionHistoryFailure.DecisionAlreadyExecuted, failure.Category);
        Assert.Null(failure.InnerException);
        Assert.Null(await service.GetByDecisionIdAsync(request.DecisionId));
        Assert.Equal(before, await f.StateAsync());
    }

    [Fact]
    public async Task Execution_links_original_model_and_actual_fractional_quantity_and_price()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var raw = new AiDecisionRecordRequest(Guid.NewGuid(), "AAPL", AiTradingSignal.Buy, 0.82m,
            new(2026, 10, 1), "historical-model", "release-17");
        var original = await new AiDecisionHistoryService(f, new AiCurrentPositionsFixture.Clock()).RecordAsync(raw);
        f.ExecutionMarket.Prices["AAPL"] = 120m;
        var engine = new AiPaperTradingEngine(f, f.ExecutionMarket, Microsoft.Extensions.Options.Options.Create(new AiRiskOptions()),
            new AiCurrentPositionsFixture.Clock());
        var request = new AiTradeExecutionRequest(Guid.NewGuid(), raw.DecisionId,
            new(true, "AAPL", AiTradingSignal.Buy, 0.82m, 100m, 1.234567899m, null));
        var executed = await engine.ExecuteAsync(request);
        var observer = new ReadObserver();
        var factory = new TrackingFactory(f.With(observer));
        var service = new AiTradesHistoryService(factory);
        var before = await f.StateAsync();
        f.ExecutionMarket.Quote = (_, _) => throw new InvalidOperationException("History must not quote.");
        f.ValuationMarket.Quote = (_, _) => throw new InvalidOperationException("History must not value positions.");
        var item = await service.GetByIdAsync(executed.TradeId);
        Assert.NotNull(item);
        Assert.Equal(request.OrderId, item.OrderId);
        Assert.Equal(raw.DecisionId, item.DecisionId);
        Assert.Equal("BUY", item.Side);
        Assert.Equal("AAPL", item.Symbol);
        Assert.Equal(1.23456789m, item.Quantity);
        Assert.Equal(120m, item.ExecutionPrice);
        Assert.Equal(148.1481m, item.TotalAmount);
        Assert.Equal(AiCurrentPositionsFixture.Now.UtcDateTime, item.ExecutedAtUtc);
        Assert.Equal(new AiTradeDecisionSummary(raw.DecisionId, AiTradingSignal.Buy, 0.82m,
            new(2026, 10, 1), "historical-model", "release-17"), item.Decision);
        Assert.Equal(item, Assert.Single(await service.GetRecentAsync(200)));
        Assert.Equal(2, observer.Commands.Count);
        Assert.All(observer.Commands, sql => Assert.Contains("LEFT JOIN", sql));
        Assert.Equal(0, factory.Tracked);
        Assert.Equal(0, observer.SaveCalls);
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(original, await new AiDecisionHistoryService(f, new AiCurrentPositionsFixture.Clock()).GetByIdAsync(raw.DecisionId));
        var calls = f.ExecutionMarket.Calls.Count;
        var retry = await engine.ExecuteAsync(request);
        Assert.Equal(executed with { IsIdempotentReplay = true }, retry);
        Assert.Equal(calls, f.ExecutionMarket.Calls.Count);
        Assert.Equal(before, await f.StateAsync());
    }

    [Fact]
    public async Task Closed_position_keeps_buy_and_sell_history_and_excludes_unexecuted_decisions_and_user_trades()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var buy = await f.ExecuteAsync(quantity: 12.5m);
        var sell = await f.ExecuteAsync(quantity: 12.5m, price: 120m, signal: AiTradingSignal.Sell);
        var msft = await f.ExecuteAsync("MSFT", 2m, 200m);
        f.ValuationMarket.Prices["MSFT"] = 250m;
        await f.RecordDecisionAsync(AiTradingSignal.Hold, AiRiskRejectionReason.HoldSignal);
        await f.RecordDecisionAsync(AiTradingSignal.Buy, AiRiskRejectionReason.LowConfidence);
        await f.RecordDecisionAsync(AiTradingSignal.Buy);
        await using var db = f.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "History user", Email = "history@example.com",
            NormalizedEmail = "HISTORY@EXAMPLE.COM", PasswordHash = "test-hash",
            CreatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime, UpdatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime };
        var portfolio = new Portfolio { Id = Guid.NewGuid(), User = user, CashBalance = 99000m, CreatedAtUtc = user.CreatedAtUtc };
        db.Transactions.Add(new() { Id = Guid.NewGuid(), Portfolio = portfolio, OrderId = Guid.NewGuid(), Side = "BUY",
            Symbol = "TSLA", Quantity = 10m, ExecutionPrice = 100m, TotalAmount = 1000m, ExecutedAtUtc = user.CreatedAtUtc });
        db.Holdings.Add(new() { Id = Guid.NewGuid(), Portfolio = portfolio, Symbol = "TSLA", Quantity = 10m,
            AverageCost = 100m, UpdatedAtUtc = user.CreatedAtUtc });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var before = await f.StateAsync();
        (await db.Users.SingleAsync()).DisplayName = "Unrelated pending edit";
        var observer = new ReadObserver();
        var service = new AiTradesHistoryService(f.With(observer));
        var history = await service.GetRecentAsync(200);
        Assert.Equal(3, history.Count);
        Assert.Contains(history, t => t.TradeId == buy.TradeId && t.Side == "BUY" && t.ExecutionPrice == 100m);
        Assert.Contains(history, t => t.TradeId == sell.TradeId && t.Side == "SELL" && t.ExecutionPrice == 120m && t.Quantity == 12.5m);
        Assert.Contains(history, t => t.TradeId == msft.TradeId && t.Side == "BUY" && t.Symbol == "MSFT" && t.Quantity == 2m);
        Assert.DoesNotContain(history, t => t.Symbol == "TSLA");
        Assert.Equal(buy.DecisionId, history.Single(t => t.TradeId == buy.TradeId).Decision!.DecisionId);
        Assert.Equal(AiTradingSignal.Sell, history.Single(t => t.TradeId == sell.TradeId).Decision!.Signal);
        Assert.Equal("MSFT", Assert.Single(await f.Service().GetCurrentAsync()).Symbol);
        Assert.Equal("MSFT", (await db.AiTraderPositions.SingleAsync()).Symbol);
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(EntityState.Modified, db.Entry(await db.Users.SingleAsync()).State);
        Assert.Equal(0, observer.SaveCalls);
        var sql = Assert.Single(observer.Commands);
        foreach (var table in new[] { "Users", "Transactions", "Holdings", "AiPortfolios", "AiPositions", "AiRejectedDecisions" })
            Assert.DoesNotContain($"\"{table}\"", sql);
    }

    [Fact]
    public async Task Legacy_trade_is_visible_with_null_association_and_persisted_values()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        var portfolio = await f.InitializeAsync();
        var trade = Trade(portfolio.PortfolioId, Guid.NewGuid(), AiCurrentPositionsFixture.Now.UtcDateTime);
        await using var db = f.CreateDbContext();
        db.AiTrades.Add(trade);
        await db.SaveChangesAsync();
        var service = new AiTradesHistoryService(f);
        var item = await service.GetByIdAsync(trade.Id);
        Assert.Equal(new AiTradeHistoryItem(trade.Id, trade.OrderId, null, "BUY", "AAPL", 1.25m, 120m, 150m,
            AiCurrentPositionsFixture.Now.UtcDateTime, null), item);
        Assert.Equal(item, Assert.Single(await service.GetRecentAsync(1)));
        Assert.Empty(await db.AiDecisions.ToArrayAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public async Task Recent_history_bounds_in_sql_and_orders_by_execution_then_id(int limit)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        var portfolio = await f.InitializeAsync();
        await using var db = f.CreateDbContext();
        var now = AiCurrentPositionsFixture.Now.UtcDateTime;
        db.AiTrades.AddRange(Trade(portfolio.PortfolioId, Id(1), now.AddDays(-1)),
            Trade(portfolio.PortfolioId, Id(2), now), Trade(portfolio.PortfolioId, Id(3), now));
        await db.SaveChangesAsync();
        var observer = new ReadObserver();
        var factory = new TrackingFactory(f.With(observer));
        var history = await new AiTradesHistoryService(factory).GetRecentAsync(limit);
        Assert.Equal(limit == 1 ? [Id(3)] : new[] { Id(3), Id(2), Id(1) }, history.Select(t => t.TradeId));
        var sql = Assert.Single(observer.Commands);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("\"ExecutedAtUtc\" DESC", sql);
        Assert.Contains("\"Id\" DESC", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Contains(limit, observer.Parameters);
        Assert.Equal(0, observer.SaveCalls);
        Assert.Equal(0, factory.Tracked);
    }

    [Fact]
    public async Task Empty_history_and_unknown_id_do_not_initialize_portfolio()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        var service = new AiTradesHistoryService(f);
        Assert.Empty(await service.GetRecentAsync(200));
        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTraderPortfolios.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public async Task Invalid_limit_fails_before_opening_database(int limit)
    {
        var service = new AiTradesHistoryService(new UnavailableFactory());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetRecentAsync(limit));
    }

    [Fact]
    public async Task Empty_id_fails_before_opening_database()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new AiTradesHistoryService(new UnavailableFactory()).GetByIdAsync(Guid.Empty));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelled_reads_propagate_before_opening_database(bool byId)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new AiTradesHistoryService(new UnavailableFactory());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (byId) await service.GetByIdAsync(Guid.NewGuid(), cancellation.Token);
            else await service.GetRecentAsync(1, cancellation.Token);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Database_failure_is_typed_without_private_details(bool byId)
    {
        var service = new AiTradesHistoryService(new UnavailableFactory());
        var ex = await Assert.ThrowsAsync<AiTradesHistoryException>(async () =>
        {
            if (byId) await service.GetByIdAsync(Guid.NewGuid());
            else await service.GetRecentAsync(1);
        });
        Assert.Equal(AiTradesHistoryFailure.PersistenceFailure, ex.Category);
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("private-storage-marker", ex.ToString());
    }

    internal static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
    internal static AiTrade Trade(Guid portfolioId, Guid id, DateTime time) => new()
    {
        Id = id, AiTraderPortfolioId = portfolioId, OrderId = Guid.NewGuid(), OrderFingerprint = new('A', 64),
        Side = "BUY", Symbol = "AAPL", Quantity = 1.25m, ExecutionPrice = 120m, TotalAmount = 150m, ExecutedAtUtc = time,
        CashBalanceAfter = 99850m, PositionQuantityAfter = 1.25m, AverageCostAfter = 120m
    };

    internal sealed class ReadObserver : DbCommandInterceptor, ISaveChangesInterceptor
    {
        public List<string> Commands { get; } = [];
        public List<object?> Parameters { get; } = [];
        public int SaveCalls { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            Parameters.AddRange(command.Parameters.Cast<DbParameter>().Select(p => p.Value));
            return ValueTask.FromResult(result);
        }
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            throw new InvalidOperationException("History must never save changes.");
        }
    }

    internal sealed class TrackingFactory(IDbContextFactory<StockLabDbContext> source) : IDbContextFactory<StockLabDbContext>
    {
        public int Tracked { get; private set; }
        public StockLabDbContext CreateDbContext()
        {
            var db = source.CreateDbContext();
            db.ChangeTracker.Tracked += (_, _) => Tracked++;
            return db;
        }
    }

    private sealed class UnavailableFactory : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => throw new SqliteException("private-storage-marker", 1);
    }
}
