using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StockLab.Application.Exceptions;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiPerformanceCycleTests
{
    [Fact]
    public async Task Captures_original_trade_model_context_and_replays_without_revaluing()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var buy = await f.ExecuteAsync();
        var clock = new Clock();
        var service = Service(f, clock);
        var first = await service.RunOnceAsync();
        Assert.False(first.IsReplay);
        Assert.Equal(100200m, first.Cycle.Valuation.TotalValue);
        var detail = await service.GetByIdAsync(first.Cycle.CaptureId);
        var trade = Assert.Single(detail!.Trades);
        Assert.Equal(buy.TradeId, trade.TradeId);
        Assert.Equal("test-model", trade.ModelName);
        Assert.Equal("test-version", trade.ModelVersion);
        Assert.NotNull(trade.DecisionId);
        Assert.Null(trade.RealizedPnL);
        await f.ExecuteAsync();
        f.ValuationMarket.Quote = (_, _) => throw new InvalidOperationException("must not revalue committed slot");
        var before = await f.StateAsync();
        var replay = await service.RunOnceAsync();
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Cycle.Valuation.Id, replay.Cycle.Valuation.Id);
        Assert.Equal(first.Cycle.Valuation.TotalValue, replay.Cycle.Valuation.TotalValue);
        Assert.Equal(before, await f.StateAsync());
        Assert.Single((await service.GetByIdAsync(first.Cycle.CaptureId))!.Trades);
        Assert.Single(await service.GetRecentAsync());
        f.ValuationMarket.Quote = null;
        clock.Now = clock.Now.AddHours(1);
        var next = await service.RunOnceAsync();
        Assert.NotEqual(first.Cycle.CaptureId, next.Cycle.CaptureId);
        Assert.Equal(2, (await service.GetByIdAsync(next.Cycle.CaptureId))!.TotalTradeCount);
        Assert.Equal(3, (await new AiTraderValuationHistoryService(f, AiTraderPerformanceTests.Portfolio(f)).GetHistoryAsync()).Count);
    }

    [Theory]
    [InlineData("portfolio")]
    [InlineData("target")]
    [InlineData("quote")]
    [InlineData("cancel")]
    [InlineData("production-mock")]
    public async Task Invalid_prerequisites_or_quotes_leave_no_capture(string failure)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        if (failure != "portfolio") { await f.InitializeAsync(); await f.ExecuteAsync(); }
        if (failure == "quote") f.ValuationMarket.Quote = (_, _) => null;
        using var cancel = new CancellationTokenSource();
        if (failure == "cancel") cancel.Cancel();
        var before = await f.StateAsync();
        var service = Service(f, target: failure == "target" ? "wrong" : "main", production: failure == "production-mock");
        Assert.NotNull(await Record.ExceptionAsync(() => service.RunOnceAsync(cancel.Token)));
        Assert.Equal(before, await f.StateAsync());
        if (failure != "quote") Assert.Empty(f.ValuationMarket.Calls);
    }

    [Fact]
    public async Task Bounded_quote_free_reads_and_invalid_input_do_not_change_state()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        await f.ExecuteAsync();
        var service = Service(f);
        var cycle = (await service.RunOnceAsync()).Cycle;
        var before = await f.StateAsync();
        f.ValuationMarket.Quote = (_, _) => throw new InvalidOperationException();
        var page = await service.GetByIdAsync(cycle.CaptureId, offset: 1, limit: 1);
        Assert.Single(page!.Trades);
        Assert.Equal(2, page.TotalTradeCount);
        Assert.Empty((await service.GetByIdAsync(cycle.CaptureId, offset: 2))!.Trades);
        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetByIdAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetByIdAsync(cycle.CaptureId, -1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetRecentAsync(201));
        Assert.Equal(before, await f.StateAsync());
    }

    internal static AiPerformanceCycleService Service(AiCurrentPositionsFixture f, Clock? clock = null,
        string target = "main", bool production = false, AiTraderValuationHistoryService? history = null)
    {
        clock ??= new Clock();
        var portfolio = new AiTraderPortfolioService(f, f.ValuationMarket, clock);
        return new(f, history ?? new AiTraderValuationHistoryService(f, portfolio),
            Options.Create(new AiPerformanceCycleOptions { ExpectedDatabase = target }), clock,
            new EnvironmentInfo { EnvironmentName = production ? Environments.Production : Environments.Development },
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MarketData:Provider"] = "Mock" }).Build());
    }

    [Fact]
    public async Task Captured_context_keeps_realized_sell_pnl_and_unknown_legacy_identity()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var known = await f.ExecuteAsync(quantity: 5m, price: 120m, signal: AiTradingSignal.Sell);
        var sell = await f.ExecuteAsync(quantity: 5m, price: 120m, signal: AiTradingSignal.Sell);
        await using (var db = f.CreateDbContext())
        {
            var legacy = await db.AiTrades.SingleAsync(t => t.Id == sell.TradeId);
            legacy.AiDecisionId = null;
            legacy.RealizedPnL = null;
            await db.SaveChangesAsync();
        }
        var service = Service(f);
        var cycle = (await service.RunOnceAsync()).Cycle;
        var trades = (await service.GetByIdAsync(cycle.CaptureId))!.Trades;
        Assert.Equal(100m, Assert.Single(trades, t => t.TradeId == known.TradeId).RealizedPnL);
        var legacyFact = Assert.Single(trades, t => t.TradeId == sell.TradeId);
        Assert.Null(legacyFact.DecisionId);
        Assert.Null(legacyFact.ModelVersion);
        Assert.Null(legacyFact.RealizedPnL);
        Assert.Equal(100200m, cycle.Valuation.TotalValue);
    }

    [Fact]
    public async Task Failed_commit_rolls_back_context_and_valuation_and_retry_can_capture()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var history = new AiTraderValuationHistoryService(f.With(new FailAfterSave()), AiTraderPerformanceTests.Portfolio(f));
        var before = await f.StateAsync();
        var error = await Assert.ThrowsAsync<AiTraderPerformanceException>(() => Service(f, history: history).RunOnceAsync());
        Assert.Equal(AiTraderPerformanceFailure.PersistenceFailure, error.Category);
        Assert.Equal(before, await f.StateAsync());
        Assert.False((await Service(f).RunOnceAsync()).IsReplay);
    }

    [Fact]
    public async Task Trade_during_quotes_cannot_publish_a_mixed_valuation_and_context()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        f.ValuationMarket.Quote = (symbol, _) =>
        {
            f.ValuationMarket.Quote = null;
            f.ExecuteAsync().GetAwaiter().GetResult();
            return new(symbol, "USD", 120m, null, null, null, AiCurrentPositionsFixture.Now);
        };
        var error = await Assert.ThrowsAsync<AiTraderPerformanceException>(() => Service(f).RunOnceAsync());
        Assert.Equal(AiTraderPerformanceFailure.ConcurrencyConflict, error.Category);
        Assert.Empty(await Service(f).GetRecentAsync());
        Assert.Equal(2, (await Service(f).GetByIdAsync((await Service(f).RunOnceAsync()).Cycle.CaptureId))!.TotalTradeCount);
    }

    internal sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new DbUpdateException("PRIVATE_STORAGE");
    }

    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = AiCurrentPositionsFixture.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class EnvironmentInfo : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "StockLab.UnitTests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
