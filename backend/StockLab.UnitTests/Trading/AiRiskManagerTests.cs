using System.Globalization;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiRiskManagerTests
{
    [Theory]
    [InlineData("0.70")]
    [InlineData("0.80")]
    [InlineData("1")]
    public async Task Buy_empty_portfolio_is_capped_by_trade_allocation(string confidence)
    {
        var portfolio = new FakePortfolio();
        var request = Request() with { Confidence = Decimal(confidence) };
        var result = await Manager(portfolio).EvaluateAsync(request);
        Approved(result, 100m);
        Assert.Equal(request.Symbol, result.Symbol);
        Assert.Equal(request.Signal, result.Signal);
        Assert.Equal(request.Confidence, result.Confidence);
        Assert.Equal(request.CurrentPrice, result.RequestedPrice);
        Assert.Equal(1, portfolio.SnapshotCalls);
        Assert.Equal(0, portfolio.StateCalls);
    }

    [Theory]
    [InlineData(AiTradingSignal.Buy, "0.6999")]
    [InlineData(AiTradingSignal.Sell, "0.60")]
    public async Task Low_confidence_short_circuits_before_portfolio(AiTradingSignal signal, string confidence)
    {
        var portfolio = new FakePortfolio();
        Rejected(await Manager(portfolio).EvaluateAsync(Request(signal) with { Confidence = Decimal(confidence) }),
            AiRiskRejectionReason.LowConfidence);
        Assert.Equal(0, portfolio.SnapshotCalls + portfolio.StateCalls);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.6999")]
    [InlineData("1")]
    public async Task Hold_never_reads_portfolio_or_produces_a_trade(string confidence)
    {
        var portfolio = new FakePortfolio();
        Rejected(await Manager(portfolio).EvaluateAsync(Request(AiTradingSignal.Hold) with { Confidence = Decimal(confidence) }),
            AiRiskRejectionReason.HoldSignal);
        Assert.Equal(0, portfolio.SnapshotCalls + portfolio.StateCalls);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("78")]
    public async Task Invalid_confidence_is_rejected_even_for_hold(string confidence)
    {
        var portfolio = new FakePortfolio();
        Rejected(await Manager(portfolio).EvaluateAsync(Request(AiTradingSignal.Hold) with { Confidence = Decimal(confidence) }),
            AiRiskRejectionReason.InvalidDecision);
        Assert.Equal(0, portfolio.StateCalls + portfolio.SnapshotCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AA PL")]
    [InlineData("AAPL\n")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFG")]
    public async Task Invalid_symbols_are_rejected(string? symbol)
    {
        Rejected(await Manager(new()).EvaluateAsync(Request() with { Symbol = symbol! }), AiRiskRejectionReason.InvalidDecision);
    }

    [Fact]
    public async Task Unknown_signal_and_null_request_fail_before_reading()
    {
        var portfolio = new FakePortfolio();
        var manager = Manager(portfolio);
        Rejected(await manager.EvaluateAsync(Request((AiTradingSignal)99)), AiRiskRejectionReason.InvalidDecision);
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.EvaluateAsync(null!));
        Assert.Equal(0, portfolio.StateCalls + portfolio.SnapshotCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_price_is_rejected_before_math(int price)
    {
        var portfolio = new FakePortfolio();
        Rejected(await Manager(portfolio).EvaluateAsync(Request() with { CurrentPrice = price }), AiRiskRejectionReason.InvalidPrice);
        Assert.Equal(0, portfolio.StateCalls + portfolio.SnapshotCalls);
    }

    [Theory]
    [InlineData("80000", "80")]
    [InlineData("120000", "120")]
    public async Task Allocation_uses_current_total_not_initial_capital(string total, string quantity)
    {
        Approved(await Manager(new() { Snapshot = Snapshot(Decimal(total), Decimal(total)) }).EvaluateAsync(Request()), Decimal(quantity));
    }

    [Fact]
    public async Task Exposure_uses_current_price_instead_of_average_cost()
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(85000m, 100000m, Position("AAPL", 150m, 100m, 1m)) };
        Approved(await Manager(portfolio).EvaluateAsync(Request()), 50m);
    }

    [Fact]
    public async Task Request_price_revalues_both_target_exposure_and_portfolio_total()
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(85000m, 98500m, Position("AAPL", 150m, 90m)) };
        Approved(await Manager(portfolio).EvaluateAsync(Request()), 50m);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(250)]
    public async Task Full_or_excess_exposure_is_rejected_before_cash(int held)
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(0m, 100000m, Position("AAPL", held, 100m)) };
        Rejected(await Manager(portfolio).EvaluateAsync(Request()), AiRiskRejectionReason.MaxSymbolExposureReached);
    }

    [Fact]
    public async Task Maximum_positions_blocks_new_symbol_but_allows_existing_symbol()
    {
        var positions = Enumerable.Range(0, 10).Select(i => Position($"S{i}", 1m, 100m)).ToArray();
        var portfolio = new FakePortfolio { Snapshot = Snapshot(99000m, 100000m, positions) };
        Rejected(await Manager(portfolio).EvaluateAsync(Request()), AiRiskRejectionReason.MaxPositionsReached);
        Approved(await Manager(portfolio).EvaluateAsync(Request() with { Symbol = "s0" }), 100m);
    }

    [Fact]
    public async Task Maximum_positions_reason_precedes_exposure_and_cash()
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(0m, 0m, Position("OTHER", 1m, 100m)) };
        Rejected(await Manager(portfolio, new() { MaxOpenPositions = 1 }).EvaluateAsync(Request()), AiRiskRejectionReason.MaxPositionsReached);
    }

    [Fact]
    public async Task Cash_can_be_exhausted_but_unrealized_value_cannot_fund_buy()
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(2500m, 100000m) };
        var result = await Manager(portfolio).EvaluateAsync(Request());
        Approved(result, 25m);
        Assert.Equal(0m, 2500m - result.ApprovedQuantity * result.RequestedPrice);
        portfolio.Snapshot = Snapshot(0m, 100000m);
        Rejected(await Manager(portfolio).EvaluateAsync(Request()), AiRiskRejectionReason.InsufficientCash);
    }

    [Fact]
    public async Task Fractional_buy_is_floored_to_eight_places_and_respects_all_limits()
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(1000m, 10000m) };
        var result = await Manager(portfolio).EvaluateAsync(Request() with { CurrentPrice = 333m });
        Approved(result, 3.00300300m);
        Assert.True(result.ApprovedQuantity * 333m <= 1000m);
    }

    [Theory]
    [InlineData("100000000000", false)]
    [InlineData("10000000000", true)]
    public async Task Minimum_quantity_is_one_eight_decimal_unit(string price, bool approved)
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(1000m, 1000m) };
        var result = await Manager(portfolio).EvaluateAsync(Request() with { CurrentPrice = Decimal(price) });
        if (approved) Approved(result, 0.00000001m);
        else Rejected(result, AiRiskRejectionReason.TradeTooSmall);
    }

    [Theory]
    [InlineData("AAPL")]
    [InlineData("aapl")]
    [InlineData("BRK.B")]
    [InlineData("TSLA:NASDAQ")]
    public async Task Sell_closes_exact_held_quantity_without_quotes_or_position_count_limit(string symbol)
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(0m, 0m, Position(symbol.ToUpperInvariant(), 12.51234567m, 100m)) };
        var result = await Manager(portfolio, new() { MaxOpenPositions = 1 }).EvaluateAsync(Request(AiTradingSignal.Sell) with { Symbol = symbol });
        Approved(result, 12.51234567m);
        Assert.Equal(symbol, result.Symbol);
        Assert.Equal(0, portfolio.SnapshotCalls);
        Assert.Equal(1, portfolio.StateCalls);
    }

    [Fact]
    public async Task Sell_absent_symbol_never_short_sells()
    {
        Rejected(await Manager(new()).EvaluateAsync(Request(AiTradingSignal.Sell)), AiRiskRejectionReason.NoPositionToSell);
    }

    [Theory]
    [InlineData(AiTradingSignal.Buy)]
    [InlineData(AiTradingSignal.Sell)]
    public async Task Non_usd_portfolio_is_rejected(AiTradingSignal signal)
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(100000m, 100000m) with { Currency = "CAD" } };
        Rejected(await Manager(portfolio).EvaluateAsync(Request(signal)), AiRiskRejectionReason.CurrencyMismatch);
    }

    [Fact]
    public async Task Snapshot_failures_propagate_without_approval_or_fallback()
    {
        foreach (var failure in new Exception[] { new HttpRequestException("Provider unavailable"), new InvalidOperationException("Quote currency mismatch") })
        {
            var portfolio = new FakePortfolio { Failure = failure };
            Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => Manager(portfolio).EvaluateAsync(Request())));
        }
    }

    [Fact]
    public async Task Cancellation_is_forwarded_and_never_becomes_rejection()
    {
        using var source = new CancellationTokenSource();
        var portfolio = new FakePortfolio();
        await Manager(portfolio).EvaluateAsync(Request(), source.Token);
        Assert.Equal(source.Token, portfolio.Token);
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Manager(portfolio).EvaluateAsync(Request(), source.Token));
    }

    [Fact]
    public async Task Arithmetic_overflow_is_a_controlled_evaluation_failure()
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(100000m, 100000m, Position("AAPL", 2m, 100m)) };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Manager(portfolio).EvaluateAsync(Request() with { CurrentPrice = decimal.MaxValue }));
        Assert.IsType<OverflowException>(error.InnerException);
    }

    [Theory]
    [InlineData("0", "99999999999.99999999")]
    [InlineData("99999999998.99999999", "1")]
    [InlineData("99999999999.99999999", "0")]
    public async Task Buy_respects_remaining_decimal_19_8_position_capacity(string held, string expected)
    {
        var quantity = Decimal(held);
        var positions = quantity == 0m ? Array.Empty<AiTraderPositionSnapshot>() : [Position("AAPL", quantity, 0.00000001m)];
        var portfolio = new FakePortfolio { Snapshot = Snapshot(100000m, 100000m + quantity * 0.00000001m, positions) };
        var result = await Manager(portfolio).EvaluateAsync(Request() with { CurrentPrice = 0.00000001m });
        if (expected == "0") Rejected(result, AiRiskRejectionReason.TradeTooSmall);
        else Approved(result, Decimal(expected));
        Assert.True(quantity + result.ApprovedQuantity <= 99999999999.99999999m);
    }

    [Theory]
    [InlineData("80000", "10")]
    [InlineData("120000", "90")]
    public async Task Exposure_ceiling_tracks_portfolio_gains_and_losses(string total, string expected)
    {
        var portfolio = new FakePortfolio { Snapshot = Snapshot(Decimal(total) - 15000m, Decimal(total), Position("AAPL", 150m, 100m)) };
        Approved(await Manager(portfolio).EvaluateAsync(Request()), Decimal(expected));
    }

    [Fact]
    public async Task Custom_policy_is_used_without_changing_signal_or_confidence()
    {
        var options = new AiRiskOptions { MinimumConfidence = 0.9m, MaxPositionExposurePercent = 0.3m, MaxCashAllocationPerTradePercent = 0.15m };
        Rejected(await Manager(new(), options).EvaluateAsync(Request()), AiRiskRejectionReason.LowConfidence);
        Approved(await Manager(new(), options).EvaluateAsync(Request() with { Confidence = 0.9m }), 150m);
    }

    private static decimal Decimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static AiRiskRequest Request(AiTradingSignal signal = AiTradingSignal.Buy) => new("AAPL", signal, 0.8m, 100m);
    private static AiRiskManager Manager(FakePortfolio portfolio, AiRiskOptions? options = null) => new(portfolio, Options.Create(options ?? new()));
    private static AiTraderPositionSnapshot Position(string symbol, decimal quantity, decimal price, decimal cost = 10m) =>
        new(symbol, quantity, cost, price, quantity * price, quantity * (price - cost));
    private static AiTraderPortfolioSnapshot Snapshot(decimal cash, decimal total, params AiTraderPositionSnapshot[] positions) =>
        new(Guid.NewGuid(), "USD", 100000m, cash, total - cash, total, total - 100000m, positions);

    private static void Approved(AiRiskDecision result, decimal quantity)
    {
        Assert.True(result.Approved);
        Assert.Null(result.RejectionReason);
        Assert.Equal(quantity, result.ApprovedQuantity);
        Assert.True(result.ApprovedQuantity > 0m);
    }

    private static void Rejected(AiRiskDecision result, AiRiskRejectionReason reason)
    {
        Assert.False(result.Approved);
        Assert.Equal(0m, result.ApprovedQuantity);
        Assert.Equal(reason, result.RejectionReason);
    }

    private sealed class FakePortfolio : IAiTraderPortfolioService
    {
        public AiTraderPortfolioSnapshot Snapshot { get; set; } = AiRiskManagerTests.Snapshot(100000m, 100000m);
        public int StateCalls { get; private set; }
        public int SnapshotCalls { get; private set; }
        public Exception? Failure { get; set; }
        public CancellationToken Token { get; private set; }
        public Task<AiTraderPortfolioState> GetOrCreateAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Risk must never initialize a portfolio.");
        public Task<AiTraderPortfolioState> GetStateAsync(CancellationToken cancellationToken = default, bool initializeIfMissing = true)
        {
            Assert.False(initializeIfMissing);
            Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            StateCalls++;
            return Task.FromResult(new AiTraderPortfolioState(Snapshot.PortfolioId, Snapshot.Currency, Snapshot.InitialCapital,
                Snapshot.CashBalance, Snapshot.Positions.Select(p => new AiTraderPositionState(p.Symbol, p.Quantity, p.AverageCost)).ToArray()));
        }
        public Task<AiTraderPortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default, bool initializeIfMissing = true)
        {
            Assert.False(initializeIfMissing);
            Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotCalls++;
            if (Failure is not null) throw Failure;
            return Task.FromResult(Snapshot);
        }
    }
}
