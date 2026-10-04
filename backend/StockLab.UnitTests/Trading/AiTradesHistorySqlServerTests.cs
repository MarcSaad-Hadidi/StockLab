using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;
using static StockLab.UnitTests.Trading.AiTradesHistoryServiceTests;

namespace StockLab.UnitTests.Trading;

public sealed class AiTradesHistorySqlServerTests
{
    private const string PreviousMigration = "20261003233535_AddAiRejectedDecisions";

    [LocalDbFact]
    public async Task Concurrent_orders_for_one_decision_commit_at_most_one_linked_trade()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        var decisionId = Guid.NewGuid();
        await new AiDecisionHistoryService(f, new AiCurrentPositionsFixture.Clock()).RecordAsync(new(decisionId,
            "AAPL", AiTradingSignal.Buy, 0.82m, new(2026, 10, 2), "model", "v1"));
        var first = new AiTradeExecutionRequest(Guid.NewGuid(), decisionId, new(true, "AAPL", AiTradingSignal.Buy, 0.82m, 100m, 100m, null));
        var second = first with { OrderId = Guid.NewGuid() };
        var gate = new QuoteGate();
        async Task<object> Execute(AiTradeExecutionRequest request)
        {
            var market = new AiPaperTradingEngineTests.FakeMarket { BeforeQuote = gate.WaitAsync };
            try { return await new AiPaperTradingEngine(f, market, Options.Create(new AiRiskOptions()), new AiCurrentPositionsFixture.Clock()).ExecuteAsync(request); }
            catch (AiTradeExecutionException ex) { return ex; }
        }
        var results = await Task.WhenAll(Execute(first), Execute(second)).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Single(results.OfType<AiTradeExecutionResult>());
        var failure = Assert.Single(results.OfType<AiTradeExecutionException>());
        Assert.Equal(AiTradeExecutionFailure.DecisionAlreadyExecuted, failure.Category);
        await using var db = f.CreateDbContext();
        var trade = await db.AiTrades.SingleAsync();
        Assert.Equal(decisionId, trade.AiDecisionId);
        Assert.Equal(90000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Equal(100m, (await db.AiTraderPositions.SingleAsync()).Quantity);
        var winning = trade.OrderId == first.OrderId ? first : second;
        var marketOnRetry = new AiPaperTradingEngineTests.FakeMarket { Quote = _ => throw new HttpRequestException() };
        var replay = await new AiPaperTradingEngine(f, marketOnRetry, Options.Create(new AiRiskOptions()),
            new AiCurrentPositionsFixture.Clock()).ExecuteAsync(winning);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal(trade.Id, replay.TradeId);
        Assert.Equal(decisionId, replay.DecisionId);
        Assert.Empty(marketOnRetry.Calls);
    }

    [LocalDbFact]
    public async Task Real_sql_history_projects_one_bounded_join_and_keeps_closed_trades()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        var buy = await f.ExecuteAsync(quantity: 12.5m);
        var sell = await f.ExecuteAsync(quantity: 12.5m, price: 120m, signal: AiTradingSignal.Sell);
        await f.RecordDecisionAsync(AiTradingSignal.Hold, AiRiskRejectionReason.HoldSignal);
        await f.RecordDecisionAsync(AiTradingSignal.Buy, AiRiskRejectionReason.LowConfidence);
        var before = await f.StateAsync();
        var observer = new ReadObserver();
        var factory = new TrackingFactory(f.With(observer));
        var service = new AiTradesHistoryService(factory);
        var history = await service.GetRecentAsync(200);
        Assert.Equal(2, history.Count);
        var sql = Assert.Single(observer.Commands);
        Assert.Contains("TOP(", sql);
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("[ExecutedAtUtc] DESC", sql);
        Assert.Contains("[Id] DESC", sql);
        Assert.Contains(200, observer.Parameters);
        Assert.Equal(history.Single(t => t.TradeId == buy.TradeId), await service.GetByIdAsync(buy.TradeId));
        Assert.Equal(2, observer.Commands.Count);
        Assert.Equal(100m, history.Single(t => t.TradeId == buy.TradeId).ExecutionPrice);
        Assert.Equal(120m, history.Single(t => t.TradeId == sell.TradeId).ExecutionPrice);
        Assert.All(history, t => Assert.NotNull(t.Decision));
        Assert.All(history, t => Assert.Equal(DateTimeKind.Utc, t.ExecutedAtUtc.Kind));
        Assert.Equal(0, observer.SaveCalls);
        Assert.Equal(0, factory.Tracked);
        Assert.Empty(await f.Service().GetCurrentAsync());
        Assert.Equal(before, await f.StateAsync());
    }

    [LocalDbFact]
    public async Task Sql_orders_equal_timestamps_by_id_before_limit()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        var portfolio = await f.InitializeAsync();
        await using var db = f.CreateDbContext();
        var now = AiCurrentPositionsFixture.Now.UtcDateTime;
        db.AiTrades.AddRange(Trade(portfolio.PortfolioId, Id(1), now.AddDays(-1)),
            Trade(portfolio.PortfolioId, Id(2), now), Trade(portfolio.PortfolioId, Id(3), now));
        await db.SaveChangesAsync();
        var service = new AiTradesHistoryService(f);
        Assert.Equal(new[] { Id(3), Id(2), Id(1) }, (await service.GetRecentAsync(200)).Select(t => t.TradeId));
        Assert.Equal(Id(3), Assert.Single(await service.GetRecentAsync(1)).TradeId);
    }

    [LocalDbFact]
    public async Task Migration_preserves_legacy_rows_and_down_removes_only_links()
    {
        // Upgrade a database created by the actual pre-#72 migrations, never an application database.
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabTradesUpgradeTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true";
        await using var f = new AiCurrentPositionsFixture(connection, null);
        await using var db = f.CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var portfolio = await f.InitializeAsync();
        var first = Trade(portfolio.PortfolioId, Id(1), AiCurrentPositionsFixture.Now.UtcDateTime);
        var second = Trade(portfolio.PortfolioId, Id(2), AiCurrentPositionsFixture.Now.UtcDateTime);
        foreach (var trade in new[] { first, second })
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AiTrades (Id, AiTraderPortfolioId, OrderId, OrderFingerprint, Side, Symbol, Quantity, ExecutionPrice, TotalAmount, ExecutedAtUtc, CashBalanceAfter, PositionQuantityAfter, AverageCostAfter) VALUES ({trade.Id}, {trade.AiTraderPortfolioId}, {trade.OrderId}, {trade.OrderFingerprint}, {trade.Side}, {trade.Symbol}, {trade.Quantity}, {trade.ExecutionPrice}, {trade.TotalAmount}, {trade.ExecutedAtUtc}, {trade.CashBalanceAfter}, {trade.PositionQuantityAfter}, {trade.AverageCostAfter})");
        await db.Database.MigrateAsync();
        var history = await new AiTradesHistoryService(f).GetRecentAsync(200);
        Assert.Equal(2, history.Count);
        Assert.All(history, item => { Assert.Null(item.DecisionId); Assert.Null(item.Decision); Assert.Equal(120m, item.ExecutionPrice); });
        Assert.Empty(await db.AiDecisions.ToArrayAsync());
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM AiTrades").SingleAsync();
        Assert.Equal(2, count);
        Assert.Equal(0, await db.AiDecisions.CountAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(2, await db.AiTrades.CountAsync());
    }

    [LocalDbFact]
    public async Task Database_rejects_missing_decision_duplicate_decision_and_deleting_linked_decision()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync(sqlServer: true);
        await f.InitializeAsync();
        var committed = await f.ExecuteAsync();
        await using var db = f.CreateDbContext();
        var portfolioId = (await db.AiTraderPortfolios.SingleAsync()).Id;
        var duplicate = Trade(portfolioId, Guid.NewGuid(), AiCurrentPositionsFixture.Now.UtcDateTime);
        duplicate.AiDecisionId = committed.DecisionId;
        db.AiTrades.Add(duplicate);
        var unique = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(unique.InnerException).Number, new[] { 2601, 2627 });
        db.ChangeTracker.Clear();
        var orphan = Trade(portfolioId, Guid.NewGuid(), AiCurrentPositionsFixture.Now.UtcDateTime);
        orphan.AiDecisionId = Guid.NewGuid();
        db.AiTrades.Add(orphan);
        var fk = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(fk.InnerException).Number);
        db.ChangeTracker.Clear();
        var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AiDecisions WHERE Id = {committed.DecisionId}"));
        Assert.Equal(547, delete.Number);
        Assert.Single(await db.AiTrades.ToListAsync());
        Assert.Single(await db.AiDecisions.ToListAsync());
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

    private sealed class LocalDbFactAttribute : FactAttribute
    {
        public LocalDbFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("STOCKLAB_TEST_LOCALDB") != "1")
                Skip = "Set STOCKLAB_TEST_LOCALDB=1 on Windows with SQL Server LocalDB.";
        }
    }
}
