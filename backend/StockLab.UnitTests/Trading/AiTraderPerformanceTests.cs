using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiTraderPerformanceTests
{
    internal static AiTraderPortfolioService Portfolio(AiCurrentPositionsFixture f) =>
        new(f, f.ValuationMarket, new AiCurrentPositionsFixture.Clock());

    internal static AiTraderPerformanceService Metrics(AiCurrentPositionsFixture f) => new(f, Portfolio(f));
    internal static AiTraderValuationHistoryService History(AiCurrentPositionsFixture f) => new(f, Portfolio(f));

    [Theory]
    [InlineData(120, 200)]
    [InlineData(80, -200)]
    [InlineData(100, 0)]
    public async Task Sell_persists_signed_realized_pnl_even_after_position_is_removed(decimal price, decimal expected)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var buy = await f.ExecuteAsync();
        var sell = await f.ExecuteAsync(price: price, signal: AiTradingSignal.Sell);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTraderPositions.ToArrayAsync());
        var row = await db.AiTrades.SingleAsync(t => t.Id == sell.TradeId);
        Assert.Equal(expected, row.RealizedPnL);
        Assert.Null((await db.AiTrades.SingleAsync(t => t.Id == buy.TradeId)).RealizedPnL);
        var metrics = await Metrics(f).GetMetricsAsync();
        Assert.Equal(100000m + expected, metrics.TotalValue);
        Assert.Equal(expected, metrics.RealizedPnL);
        Assert.Equal(expected, metrics.TotalPnL);
        Assert.Equal(expected / 100000m, metrics.TotalReturn);
        Assert.Equal(0m, metrics.UnrealizedPnL);
        Assert.Equal(sell.TradeId, metrics.BestTrade!.TradeId);
        Assert.Equal(sell.TradeId, metrics.WorstTrade!.TradeId);
        Assert.Equal(expected > 0m ? 1m : 0m, metrics.WinRate);
    }

    [Fact]
    public async Task Empty_portfolio_has_no_invented_sales_or_drawdown_and_reads_do_not_write()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var before = await f.StateAsync();
        var m = await Metrics(f).GetMetricsAsync();
        Assert.Equal(100000m, m.InitialCapital);
        Assert.Equal(100000m, m.CashBalance);
        Assert.Equal(100000m, m.TotalValue);
        Assert.Equal(0m, m.TotalReturn);
        Assert.Equal(0m, m.TotalPnL);
        Assert.Equal(0m, m.WinRate);
        Assert.Equal(0m, m.RealizedPnL);
        Assert.Null(m.AverageWin);
        Assert.Null(m.AverageLoss);
        Assert.Null(m.BestTrade);
        Assert.Null(m.WorstTrade);
        Assert.Null(m.MaxDrawdown);
        Assert.Equal(0, m.ExecutedTradeCount);
        Assert.Equal(0, m.OpenPositionsCount);
        Assert.Equal(1, m.HistoricalCoverage.ValuationSampleCount);
        Assert.True(m.TradeStatisticsComplete);
        Assert.True(Assert.Single(await History(f).GetHistoryAsync()).IsInitialBaseline);
        Assert.Empty(f.ValuationMarket.Calls);
        Assert.Equal(before, await f.StateAsync());
    }

    [Fact]
    public async Task Missing_portfolio_never_initializes_on_metrics_history_or_capture()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        foreach (var read in new Func<Task>[] { () => Metrics(f).GetMetricsAsync(),
            () => History(f).GetHistoryAsync(), () => History(f).CaptureCurrentAsync(Guid.NewGuid()) })
            await Assert.ThrowsAsync<AiTraderPerformanceException>(read);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTraderPortfolios.ToArrayAsync());
        Assert.Empty(await db.AiTraderValuationSnapshots.ToArrayAsync());
    }

    [Fact]
    public async Task Multiple_sells_keep_losses_signed_and_break_even_in_win_rate_denominator()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync(quantity: 50m);
        foreach (var price in new[] { 110m, 130m, 95m, 85m, 100m })
            await f.ExecuteAsync(quantity: 10m, price: price, signal: AiTradingSignal.Sell);
        var m = await Metrics(f).GetMetricsAsync();
        Assert.Equal(6, m.ExecutedTradeCount);
        Assert.Equal(1, m.BuyTradeCount);
        Assert.Equal(5, m.SellTradeCount);
        Assert.Equal(2, m.WinningSellCount);
        Assert.Equal(2, m.LosingSellCount);
        Assert.Equal(1, m.BreakEvenSellCount);
        Assert.Equal(.4m, m.WinRate);
        Assert.Equal(200m, m.AverageWin);
        Assert.Equal(-100m, m.AverageLoss);
        Assert.Equal(300m, m.BestTrade!.RealizedPnL);
        Assert.Equal(-150m, m.WorstTrade!.RealizedPnL);
        Assert.Equal(200m, m.RealizedPnL);
    }

    [Fact]
    public async Task Real_execution_smoke_reuses_weighted_cost_history_risk_and_observed_valuations()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await VerifySmokeAsync(f);
    }

    internal static async Task VerifySmokeAsync(AiCurrentPositionsFixture f)
    {
        await f.InitializeAsync();
        var risk = new AiRiskManager(Portfolio(f), Options.Create(new AiRiskOptions()));
        var approval = await risk.EvaluateAsync(new("AAPL", AiTradingSignal.Buy, .9m, 100m));
        Assert.True(approval.Approved);
        var request = await f.RecordExecutionAsync();
        await f.ExecuteAsync(request with { RiskDecision = approval with { ApprovedQuantity = 10m } });
        await f.ExecuteAsync(price: 120m);
        f.ValuationMarket.Prices["AAPL"] = 130m;
        await History(f).CaptureCurrentAsync(Guid.NewGuid());
        var sellRequest = await f.RecordExecutionAsync(quantity: 4m, price: 140m, signal: AiTradingSignal.Sell);
        var partial = await f.ExecuteAsync(sellRequest);
        Assert.Equal(110m, partial.AverageCost);
        Assert.Equal(16m, partial.PositionQuantity);
        var replay = await f.ExecuteAsync(sellRequest);
        Assert.True(replay.IsIdempotentReplay);
        f.ValuationMarket.Prices["AAPL"] = 100m;
        var m = await Metrics(f).GetMetricsAsync();
        Assert.Equal(98360m, m.CashBalance);
        Assert.Equal(1600m, m.PositionsMarketValue);
        Assert.Equal(99960m, m.TotalValue);
        Assert.Equal(-40m, m.TotalPnL);
        Assert.Equal(120m, m.RealizedPnL);
        Assert.Equal(-160m, m.UnrealizedPnL);
        await History(f).CaptureCurrentAsync(Guid.NewGuid());
        await f.ExecuteAsync(quantity: 16m, price: 105m, signal: AiTradingSignal.Sell);
        var final = await Metrics(f).GetMetricsAsync();
        Assert.Equal(100040m, final.TotalValue);
        Assert.Equal(40m, final.RealizedPnL);
        Assert.Equal(0m, final.UnrealizedPnL);
        Assert.Equal(4, final.ExecutedTradeCount);
        Assert.Equal(.5m, final.WinRate);
        Assert.Equal(120m, final.AverageWin);
        Assert.Equal(-80m, final.AverageLoss);
        Assert.Equal(4, (await new AiTradesHistoryService(f).GetRecentAsync(200)).Count);
        Assert.Empty(await f.Service().GetCurrentAsync());
    }

    [Fact]
    public async Task Unknown_legacy_sale_makes_lifetime_sell_statistics_unavailable()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var sell = await f.ExecuteAsync(price: 120m, signal: AiTradingSignal.Sell);
        await using var db = f.CreateDbContext();
        (await db.AiTrades.SingleAsync(t => t.Id == sell.TradeId)).RealizedPnL = null;
        await db.SaveChangesAsync();
        var m = await Metrics(f).GetMetricsAsync();
        Assert.False(m.TradeStatisticsComplete);
        Assert.Equal(1, m.UnvaluedLegacySellCount);
        Assert.Null(m.RealizedPnL);
        Assert.Null(m.WinRate);
        Assert.Null(m.AverageWin);
        Assert.Null(m.AverageLoss);
        Assert.Null(m.BestTrade);
        Assert.Null(m.WorstTrade);
        Assert.Equal(200m, m.TotalPnL);
    }

    [Fact]
    public async Task Fractional_sale_uses_rounded_settlement_and_stored_average_cost()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync(quantity: 3.12500001m, price: 80.1234m);
        var sale = await f.ExecuteAsync(quantity: 1.12500001m, price: 100.5678m, signal: AiTradingSignal.Sell);
        await using var db = f.CreateDbContext();
        Assert.Equal(23m, (await db.AiTrades.SingleAsync(t => t.Id == sale.TradeId)).RealizedPnL);
        Assert.Equal(2m, sale.PositionQuantity);
        Assert.Equal(80.1234m, sale.AverageCost);
    }

    [Fact]
    public async Task Best_and_worst_ties_choose_latest_timestamp_then_largest_trade_id()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync(quantity: 30m);
        for (var i = 0; i < 3; i++) await f.ExecuteAsync(price: 120m, signal: AiTradingSignal.Sell);
        await using var db = f.CreateDbContext();
        var expected = await db.AiTrades.Where(t => t.Side == "SELL")
            .OrderByDescending(t => t.ExecutedAtUtc).ThenByDescending(t => t.Id).Select(t => t.Id).FirstAsync();
        var m = await Metrics(f).GetMetricsAsync();
        Assert.Equal(expected, m.BestTrade!.TradeId);
        Assert.Equal(expected, m.WorstTrade!.TradeId);
        Assert.Null(m.AverageLoss);
    }

    [Fact]
    public async Task Decisions_rejections_hold_and_user_finances_never_enter_bot_statistics()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.RecordDecisionAsync(AiTradingSignal.Buy, AiRiskRejectionReason.LowConfidence);
        await f.RecordDecisionAsync(AiTradingSignal.Hold);
        await using var db = f.CreateDbContext();
        foreach (var email in new[] { "one@example.com", "two@example.com" })
        {
            var user = new User { Id = Guid.NewGuid(), DisplayName = "test", Email = email,
                NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "test", CreatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime,
                UpdatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime };
            var p = new Portfolio { Id = Guid.NewGuid(), User = user, CashBalance = 90000m,
                CreatedAtUtc = user.CreatedAtUtc };
            db.Holdings.Add(new() { Id = Guid.NewGuid(), Portfolio = p, Symbol = "MSFT", Quantity = 100m,
                AverageCost = 100m, UpdatedAtUtc = user.CreatedAtUtc });
            db.Transactions.Add(new() { Id = Guid.NewGuid(), Portfolio = p, OrderId = Guid.NewGuid(), Side = "BUY",
                Symbol = "MSFT", Quantity = 100m, ExecutionPrice = 100m, TotalAmount = 10000m, ExecutedAtUtc = user.CreatedAtUtc });
        }
        await db.SaveChangesAsync();
        var before = await f.StateAsync();
        var m = await Metrics(f).GetMetricsAsync();
        Assert.Equal(0, m.ExecutedTradeCount);
        Assert.Equal(0m, m.TotalPnL);
        Assert.Equal(before, await f.StateAsync());
    }
}
