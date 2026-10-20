using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Trading;
using static StockLab.UnitTests.Trading.AiTraderPerformanceTests;

namespace StockLab.UnitTests.Trading;

public sealed class AiTraderValuationHistoryTests
{
    [Fact]
    public async Task Capture_preserves_quote_observation_time_and_replays_only_identical_content()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var observed = AiCurrentPositionsFixture.Now.AddDays(-2);
        f.ValuationMarket.Quote = (symbol, _) => new(symbol, "USD", 120m, null, null, null, observed);
        var id = Guid.NewGuid();
        var point = await History(f).CaptureCurrentAsync(id);
        Assert.Equal(100200m, point.TotalValue);
        Assert.Equal(200m, point.PnL);
        Assert.Equal(.002m, point.Return);
        Assert.Equal(AiCurrentPositionsFixture.Now.UtcDateTime, point.ValuedAtUtc);
        Assert.Equal(observed, Assert.Single(point.QuoteObservations).AsOfUtc);
        Assert.Equal(point.Id, (await History(f).CaptureCurrentAsync(id)).Id);
        f.ValuationMarket.Quote = (symbol, _) => new(symbol, "USD", 120.00m, null, null, null, observed);
        Assert.Equal(point.Id, (await History(f).CaptureCurrentAsync(id)).Id);
        f.ValuationMarket.Quote = (symbol, _) => new(symbol, "USD", 80m, null, null, null, observed);
        var error = await Assert.ThrowsAsync<AiTraderPerformanceException>(() => History(f).CaptureCurrentAsync(id));
        Assert.Equal(AiTraderPerformanceFailure.CaptureConflict, error.Category);
        Assert.Equal(100200m, (await History(f).GetHistoryAsync()).Last().TotalValue);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("currency")]
    [InlineData("symbol")]
    [InlineData("default-time")]
    [InlineData("future-time")]
    [InlineData("offset")]
    [InlineData("provider")]
    public async Task Invalid_quotes_never_become_historical_losses(string failure)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        f.ValuationMarket.Quote = (symbol, _) => failure switch
        {
            "missing" => null,
            "provider" => throw new HttpRequestException("private-provider-marker"),
            _ => new(failure == "symbol" ? "MSFT" : symbol, failure == "currency" ? "CAD" : "USD",
                failure == "zero" ? 0m : failure == "negative" ? -1m : 120m, null, null, null,
                failure == "default-time" ? default : failure == "future-time" ? AiCurrentPositionsFixture.Now.AddHours(1)
                    : failure == "offset" ? AiCurrentPositionsFixture.Now.ToOffset(TimeSpan.FromHours(1)) : AiCurrentPositionsFixture.Now)
        };
        var before = await f.StateAsync();
        var error = await Record.ExceptionAsync(() => History(f).CaptureCurrentAsync(Guid.NewGuid()));
        Assert.NotNull(error);
        if (failure == "provider") Assert.IsType<HttpRequestException>(error);
        Assert.Equal(before, await f.StateAsync());
        Assert.Single(await History(f).GetHistoryAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trade_during_quotes_returns_controlled_conflict_without_mixed_state(bool capture)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        f.ValuationMarket.Quote = (symbol, _) =>
        {
            f.ValuationMarket.Quote = null;
            f.ExecuteAsync(price: 120m).GetAwaiter().GetResult();
            return new(symbol, "USD", 120m, null, null, null, AiCurrentPositionsFixture.Now);
        };
        var error = await Assert.ThrowsAsync<AiTraderPerformanceException>(() => capture
            ? (Task)History(f).CaptureCurrentAsync(Guid.NewGuid()) : Metrics(f).GetMetricsAsync());
        Assert.Equal(AiTraderPerformanceFailure.ConcurrencyConflict, error.Category);
        Assert.Single(await History(f).GetHistoryAsync());
        Assert.Equal(2, (await Metrics(f).GetMetricsAsync()).BuyTradeCount);
    }

    [Fact]
    public async Task Historical_drawdown_uses_all_observations_even_when_chart_limit_is_small()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        var state = await f.InitializeAsync();
        await using var db = f.CreateDbContext();
        // Deliberately persisted valuation facts exercise historical math independently of today's quotes.
        foreach (var (value, day) in new[] { (120000m, 1), (90000m, 2), (110000m, 3) })
            db.AiTraderValuationSnapshots.Add(new() { Id = Guid.NewGuid(), AiTraderPortfolioId = state.PortfolioId,
                CaptureId = Guid.NewGuid(), CashBalance = value, Currency = "USD",
                ValuedAtUtc = AiCurrentPositionsFixture.Now.AddDays(day).UtcDateTime });
        await db.SaveChangesAsync();
        var m = await Metrics(f).GetMetricsAsync();
        Assert.Equal(.25m, m.MaxDrawdown);
        Assert.Equal(4, m.HistoricalCoverage.ValuationSampleCount);
        Assert.Equal(110000m, Assert.Single(await History(f).GetHistoryAsync(limit: 1)).TotalValue);
        Assert.Equal(new[] { 100000m, 120000m, 90000m, 110000m },
            (await History(f).GetHistoryAsync()).Select(p => p.TotalValue));
    }

    [Fact]
    public async Task Legacy_portfolio_without_history_has_unknown_drawdown_and_no_synthetic_baseline()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await using var db = f.CreateDbContext();
        db.AiTraderPortfolios.Add(new() { Id = Guid.NewGuid(), CashBalance = 90000m,
            CreatedAtUtc = AiCurrentPositionsFixture.Now.AddDays(-10).UtcDateTime,
            UpdatedAtUtc = AiCurrentPositionsFixture.Now.UtcDateTime });
        await db.SaveChangesAsync();
        Assert.Empty(await History(f).GetHistoryAsync());
        var m = await Metrics(f).GetMetricsAsync();
        Assert.Null(m.MaxDrawdown);
        Assert.Equal(AiValuationCoverageStatus.Absent, m.HistoricalCoverage.Status);
        await History(f).CaptureCurrentAsync(Guid.NewGuid());
        Assert.Null((await Metrics(f).GetMetricsAsync()).MaxDrawdown);
        Assert.False(Assert.Single(await History(f).GetHistoryAsync()).IsInitialBaseline);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task History_requires_bounded_limits(int limit)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => History(f).GetHistoryAsync(limit: limit));
    }

    [Fact]
    public async Task History_rejects_non_utc_reversed_and_default_ranges_and_empty_capture_id()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        var now = AiCurrentPositionsFixture.Now.UtcDateTime;
        await Assert.ThrowsAsync<ArgumentException>(() => History(f).GetHistoryAsync(now, now.AddDays(-1)));
        await Assert.ThrowsAsync<ArgumentException>(() => History(f).GetHistoryAsync(DateTime.SpecifyKind(now, DateTimeKind.Unspecified)));
        await Assert.ThrowsAsync<ArgumentException>(() => History(f).GetHistoryAsync(default(DateTime)));
        await Assert.ThrowsAsync<ArgumentException>(() => History(f).CaptureCurrentAsync(Guid.Empty));
    }

    [Fact]
    public async Task Capture_rolls_back_database_failure_and_honors_cancellation_and_append_only_history()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        var history = new AiTraderValuationHistoryService(f.With(new FailAfterSave()), Portfolio(f));
        var error = await Assert.ThrowsAsync<AiTraderPerformanceException>(() => history.CaptureCurrentAsync(Guid.NewGuid()));
        Assert.Equal(AiTraderPerformanceFailure.PersistenceFailure, error.Category);
        Assert.Single(await History(f).GetHistoryAsync());
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => History(f).CaptureCurrentAsync(Guid.NewGuid(), cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => History(f).GetHistoryAsync(cancellationToken: cancel.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Metrics(f).GetMetricsAsync(cancel.Token));
        await using var db = f.CreateDbContext();
        var point = await db.AiTraderValuationSnapshots.SingleAsync();
        db.Entry(point).Property(p => p.CashBalance).CurrentValue = 1m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new DbUpdateException("private-storage-marker");
    }

    [Fact]
    public async Task History_filters_inclusive_utc_bounds_before_limiting_and_preserves_recorded_values()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        await History(f).CaptureCurrentAsync(Guid.NewGuid());
        var now = AiCurrentPositionsFixture.Now.UtcDateTime;
        Assert.Equal(2, (await History(f).GetHistoryAsync(now, now)).Count);
        Assert.Empty(await History(f).GetHistoryAsync(now.AddTicks(1)));
        f.ValuationMarket.Prices["AAPL"] = 1m;
        Assert.Equal(100200m, (await History(f).GetHistoryAsync()).Last().TotalValue);
    }

    [Fact]
    public async Task Read_queries_never_save_or_flush_pending_changes_and_quote_once_per_position()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var before = await f.StateAsync();
        await using var caller = f.CreateDbContext();
        (await caller.AiTraderPortfolios.SingleAsync()).CashBalance = 1m;
        var observer = new AiCurrentPositionsFixture.ReadObserver();
        var factory = f.With(observer);
        await new AiTraderPerformanceService(factory, new AiTraderPortfolioService(factory, f.ValuationMarket,
            new AiCurrentPositionsFixture.Clock())).GetMetricsAsync();
        await new AiTraderValuationHistoryService(factory, Portfolio(f)).GetHistoryAsync();
        Assert.Equal(new[] { "AAPL" }, f.ValuationMarket.Calls);
        Assert.Equal(0, observer.SaveCalls);
        Assert.Equal(before, await f.StateAsync());
        Assert.Equal(EntityState.Modified, caller.Entry(await caller.AiTraderPortfolios.SingleAsync()).State);
        Assert.All(observer.Commands, command =>
        {
            foreach (var table in new[] { "Users", "Portfolios", "Holdings", "Transactions", "AiDecisions", "AiRejectedDecisions" })
                Assert.DoesNotContain($"\"{table}\"", command);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_rejects_storage_capacity_and_decimal_overflow_without_writing(bool overflow)
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        f.ValuationMarket.Prices["AAPL"] = overflow ? decimal.MaxValue : 999999999999999.9999m;
        var before = await f.StateAsync();
        var error = await Assert.ThrowsAsync<AiTraderPerformanceException>(() => History(f).CaptureCurrentAsync(Guid.NewGuid()));
        Assert.Equal(AiTraderPerformanceFailure.StorageCapacityExceeded, error.Category);
        Assert.Equal(before, await f.StateAsync());
    }
}
