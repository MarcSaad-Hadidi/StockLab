using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiPaperTradingEngineTests
{
    [Fact]
    public async Task Unrecorded_raw_decision_cannot_execute_or_mutate_portfolio()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var error = await Assert.ThrowsAsync<AiTradeExecutionException>(() => f.Engine().ExecuteAsync(Request()));
        Assert.Equal(AiTradeExecutionFailure.DecisionNotFound, error.Category);
        Assert.Empty(f.Market.Calls);
        await f.AssertUnchangedAsync();
    }

    [Fact]
    public async Task Empty_decision_id_fails_before_quotes_and_mutations()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        await Failure(f.Engine(), Request() with { DecisionId = Guid.Empty }, AiTradeExecutionFailure.InvalidDecision);
        Assert.Empty(f.Market.Calls);
        await f.AssertUnchangedAsync();
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("symbol-case")]
    [InlineData("signal")]
    [InlineData("confidence")]
    [InlineData("confidence-precision")]
    [InlineData("raw-hold")]
    public async Task Raw_decision_must_match_unmodified_risk_approval_exactly(string mismatch)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var request = Request();
        await f.RecordAsync(mismatch == "raw-hold" ? request with { RiskDecision = request.RiskDecision with { Signal = AiTradingSignal.Hold } } : request);
        var risk = request.RiskDecision;
        risk = mismatch switch
        {
            "symbol" => risk with { Symbol = "MSFT" },
            "symbol-case" => risk with { Symbol = "aapl" },
            "signal" => risk with { Signal = AiTradingSignal.Sell },
            "confidence" => risk with { Confidence = 0.81m },
            "confidence-precision" => risk with { Confidence = 0.8000000000000000000000000001m },
            _ => risk
        };
        await Failure(f.Engine(), request with { RiskDecision = risk }, mismatch == "raw-hold"
            ? AiTradeExecutionFailure.HoldNotExecutable : AiTradeExecutionFailure.DecisionMismatch);
        Assert.Empty(f.Market.Calls);
        await f.AssertUnchangedAsync();
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiDecisions.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejection_before_execution_or_during_quote_prevents_commit(bool duringQuote)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var request = await f.RequestAsync();
        async Task Reject()
        {
            await new AiRejectedDecisionHistoryService(f, new FixedClock()).RecordAsync(new(request.DecisionId,
                request.RiskDecision with { Approved = false, ApprovedQuantity = 0m, RejectionReason = AiRiskRejectionReason.LowConfidence }));
        }
        if (duringQuote) f.Market.BeforeQuote = Reject;
        else await Reject();
        await Failure(f.Engine(), request, AiTradeExecutionFailure.DecisionRejected);
        if (!duringQuote) Assert.Empty(f.Market.Calls);
        await f.AssertUnchangedAsync();
        await using var db = f.CreateDbContext();
        var rejected = await db.AiRejectedDecisions.SingleAsync();
        Assert.Equal(request.DecisionId, rejected.AiDecisionId);
        Assert.Equal("LowConfidence", rejected.RejectionReason);
    }

    [Fact]
    public async Task Same_decision_under_another_order_cannot_execute_twice()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var request = await f.RequestAsync();
        var first = await f.Engine().ExecuteAsync(request);
        Assert.Equal(request.DecisionId, first.DecisionId);
        f.Market.Quote = _ => throw new HttpRequestException();
        await Failure(f.Engine(), request with { OrderId = Guid.NewGuid() }, AiTradeExecutionFailure.DecisionAlreadyExecuted);
        await using var db = f.CreateDbContext();
        var trade = await db.AiTrades.SingleAsync();
        Assert.Equal(request.DecisionId, trade.AiDecisionId);
        Assert.Equal(90000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Equal(100m, (await db.AiTraderPositions.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Same_order_with_another_matching_decision_is_a_conflict()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var first = await f.RequestAsync();
        await f.Engine().ExecuteAsync(first);
        var other = first with { DecisionId = Guid.NewGuid() };
        await f.RecordAsync(other);
        await Failure(f.Engine(), other, AiTradeExecutionFailure.DuplicateOrder);
        await using var db = f.CreateDbContext();
        Assert.Equal(first.DecisionId, (await db.AiTrades.SingleAsync()).AiDecisionId);
        Assert.Equal(90000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [Fact]
    public async Task Buy_creates_position_and_trade_using_server_price_and_one_timestamp()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var request = await f.RequestAsync();
        var result = await f.Engine().ExecuteAsync(request);
        Assert.Equal(90000m, result.CashBalance);
        Assert.Equal(100m, result.ExecutedQuantity);
        Assert.Equal(100m, result.AverageCost);
        Assert.Equal(10000m, result.TotalAmount);
        Assert.Equal(Fixture.Now.UtcDateTime, result.ExecutedAtUtc);
        await using var db = f.CreateDbContext();
        var position = await db.AiTraderPositions.SingleAsync();
        var portfolio = await db.AiTraderPortfolios.SingleAsync();
        var trade = await db.AiTrades.SingleAsync();
        Assert.Equal(result.TradeId, trade.Id);
        Assert.Equal(request.OrderId, trade.OrderId);
        Assert.Equal(portfolio.Id, trade.AiTraderPortfolioId);
        Assert.Equal("BUY", trade.Side);
        Assert.Equal("AAPL", trade.Symbol);
        Assert.Equal(result.ExecutedQuantity, trade.Quantity);
        Assert.Equal(result.ExecutionPrice, trade.ExecutionPrice);
        Assert.Equal(result.TotalAmount, trade.TotalAmount);
        Assert.Equal(100m, position.Quantity);
        Assert.Equal(result.ExecutedAtUtc, portfolio.UpdatedAtUtc);
        Assert.Equal(result.ExecutedAtUtc, position.CreatedAtUtc);
        Assert.Equal(result.ExecutedAtUtc, position.UpdatedAtUtc);
        Assert.False(result.IsIdempotentReplay);
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Portfolios.ToListAsync());
        Assert.Empty(await db.Holdings.ToListAsync());
        Assert.Empty(await db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task Buy_existing_position_uses_weighted_cost_and_preserves_creation_time()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(99000m, ("AAPL", 10m, 100m));
        f.Market.Price = 120m;
        var result = await f.Engine().ExecuteAsync(await f.RequestAsync(quantity: 10m));
        Assert.Equal(97800m, result.CashBalance);
        Assert.Equal(20m, result.PositionQuantity);
        Assert.Equal(110m, result.AverageCost);
        await using var db = f.CreateDbContext();
        Assert.Equal(Fixture.Before, (await db.AiTraderPositions.SingleAsync()).CreatedAtUtc);
        Assert.Single(await db.AiTrades.ToListAsync());
    }

    [Theory]
    [InlineData("12.5", "0", "100000")]
    [InlineData("2.5", "10", "98800")]
    public async Task Sell_closes_or_reduces_holding_without_changing_remaining_cost(string quantity, string remaining, string cash)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(98500m, ("AAPL", 12.5m, 100m));
        f.Market.Price = 120m;
        var result = await f.Engine().ExecuteAsync(await f.RequestAsync(AiTradingSignal.Sell, D(quantity)));
        Assert.Equal(D(cash), result.CashBalance);
        Assert.Equal(D(remaining), result.PositionQuantity);
        Assert.Equal(D(remaining) == 0m ? null : 100m, result.AverageCost);
        await using var db = f.CreateDbContext();
        Assert.Equal("SELL", (await db.AiTrades.SingleAsync()).Side);
        Assert.Equal(D(remaining) == 0m ? 0 : 1, await db.AiTraderPositions.CountAsync());
        if (D(remaining) > 0m)
        {
            var position = await db.AiTraderPositions.SingleAsync();
            Assert.Equal(100m, position.AverageCost);
            Assert.Equal(Fixture.Before, position.CreatedAtUtc);
            Assert.Equal(Fixture.Now.UtcDateTime, position.UpdatedAtUtc);
        }
    }

    [Theory]
    [InlineData("110", "90.90909090", "90000")]
    [InlineData("90", "100", "91000")]
    public async Task Fresh_price_resizes_buy_down_but_never_above_approval(string price, string quantity, string cash)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        f.Market.Price = D(price);
        var result = await f.Engine().ExecuteAsync(await f.RequestAsync());
        Assert.Equal(D(quantity), result.ExecutedQuantity);
        Assert.Equal(D(cash), result.CashBalance);
        Assert.Equal(100m, result.RiskPrice);
        Assert.Equal(100m, result.RiskApprovedQuantity);
        Assert.Equal(D(price), result.ExecutionPrice);
    }

    [Theory]
    [InlineData("empty-id", AiTradeExecutionFailure.InvalidDecision)]
    [InlineData("rejected", AiTradeExecutionFailure.RiskRejected)]
    [InlineData("hold", AiTradeExecutionFailure.HoldNotExecutable)]
    [InlineData("zero", AiTradeExecutionFailure.InvalidQuantity)]
    [InlineData("negative", AiTradeExecutionFailure.InvalidQuantity)]
    [InlineData("incoherent", AiTradeExecutionFailure.InvalidDecision)]
    [InlineData("symbol", AiTradeExecutionFailure.InvalidDecision)]
    [InlineData("confidence", AiTradeExecutionFailure.InvalidDecision)]
    [InlineData("signal", AiTradeExecutionFailure.InvalidDecision)]
    [InlineData("risk-price", AiTradeExecutionFailure.InvalidDecision)]
    public async Task Invalid_approval_never_executes(string scenario, AiTradeExecutionFailure expected)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var request = await f.RequestAsync();
        request = scenario switch
        {
            "empty-id" => request with { OrderId = Guid.Empty },
            "rejected" => request with { RiskDecision = request.RiskDecision with { Approved = false } },
            "hold" => request with { RiskDecision = request.RiskDecision with { Signal = AiTradingSignal.Hold } },
            "zero" => request with { RiskDecision = request.RiskDecision with { ApprovedQuantity = 0m } },
            "negative" => request with { RiskDecision = request.RiskDecision with { ApprovedQuantity = -1m } },
            "incoherent" => request with { RiskDecision = request.RiskDecision with { RejectionReason = AiRiskRejectionReason.LowConfidence } },
            "symbol" => request with { RiskDecision = request.RiskDecision with { Symbol = "AA PL" } },
            "confidence" => request with { RiskDecision = request.RiskDecision with { Confidence = 1.1m } },
            "signal" => request with { RiskDecision = request.RiskDecision with { Signal = (AiTradingSignal)99 } },
            _ => request with { RiskDecision = request.RiskDecision with { RequestedPrice = 0m } }
        };
        await Failure(f.Engine(), request, expected);
        Assert.Empty(f.Market.Calls);
        await f.AssertUnchangedAsync();
    }

    [Theory]
    [InlineData(AiTradingSignal.Buy)]
    [InlineData(AiTradingSignal.Sell)]
    public async Task Batch_symbols_fail_before_market_access_without_database_mutation(AiTradingSignal signal)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var request = await f.RequestAsync(signal);
        request = request with { RiskDecision = request.RiskDecision with { Symbol = "AAPL,MSFT" } };
        await Failure(f.Engine(), request, AiTradeExecutionFailure.InvalidDecision);
        Assert.Empty(f.Market.Calls);
        await f.AssertUnchangedAsync();
    }

    [Theory]
    [InlineData("missing", AiTradeExecutionFailure.QuoteUnavailable)]
    [InlineData("throw", AiTradeExecutionFailure.QuoteUnavailable)]
    [InlineData("zero", AiTradeExecutionFailure.InvalidExecutionPrice)]
    [InlineData("negative", AiTradeExecutionFailure.InvalidExecutionPrice)]
    [InlineData("tiny", AiTradeExecutionFailure.InvalidExecutionPrice)]
    [InlineData("overflow", AiTradeExecutionFailure.InvalidExecutionPrice)]
    [InlineData("currency", AiTradeExecutionFailure.CurrencyMismatch)]
    [InlineData("symbol", AiTradeExecutionFailure.InvalidExecutionPrice)]
    public async Task Invalid_quote_never_falls_back_to_risk_price(string scenario, AiTradeExecutionFailure expected)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        f.Market.Quote = symbol => scenario switch
        {
            "missing" => null,
            "throw" => throw new HttpRequestException("Private upstream details"),
            "zero" => Quote(symbol, 0m),
            "negative" => Quote(symbol, -1m),
            "tiny" => Quote(symbol, 0.00001m),
            "overflow" => Quote(symbol, decimal.MaxValue),
            "currency" => Quote(symbol, 100m) with { Currency = "CAD" },
            _ => Quote("OTHER", 100m)
        };
        await Failure(f.Engine(), await f.RequestAsync(), expected);
        await f.AssertUnchangedAsync();
    }

    [Fact]
    public async Task Missing_portfolio_never_creates_capital()
    {
        await using var f = await Fixture.CreateAsync();
        await Failure(f.Engine(), await f.RequestAsync(), AiTradeExecutionFailure.PortfolioNotFound);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTraderPortfolios.ToListAsync());
        Assert.Empty(await db.AiTrades.ToListAsync());
    }

    [Fact]
    public async Task Retry_returns_original_committed_response_even_after_another_trade_and_quote_failure()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var request = Request() with { RiskDecision = Request().RiskDecision with { Symbol = "aapl" } };
        await f.RecordAsync(request);
        var first = await f.Engine().ExecuteAsync(request);
        await f.Engine().ExecuteAsync(await f.RequestAsync(AiTradingSignal.Sell, 50m));
        f.Market.Quote = _ => throw new HttpRequestException();
        var replay = await f.Engine().ExecuteAsync(request with { RiskDecision = request.RiskDecision with { Symbol = "AAPL" } });
        Assert.Equal(first with { IsIdempotentReplay = true }, replay);
        await using var db = f.CreateDbContext();
        Assert.Equal(2, await db.AiTrades.CountAsync());
        Assert.Equal(95000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Equal(50m, (await db.AiTraderPositions.SingleAsync()).Quantity);
    }

    [Theory]
    [InlineData("symbol")]
    [InlineData("side")]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("confidence")]
    public async Task Reusing_order_id_for_a_different_approval_is_rejected(string field)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        var request = await f.RequestAsync();
        await f.Engine().ExecuteAsync(request);
        var decision = request.RiskDecision;
        decision = field switch
        {
            "symbol" => decision with { Symbol = "MSFT" },
            "side" => decision with { Signal = AiTradingSignal.Sell },
            "quantity" => decision with { ApprovedQuantity = 99m },
            "price" => decision with { RequestedPrice = 101m },
            _ => decision with { Confidence = 0.9m }
        };
        await Failure(f.Engine(), request with { RiskDecision = decision }, AiTradeExecutionFailure.DuplicateOrder);
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiTrades.ToListAsync());
        Assert.Equal(90000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [Theory]
    [InlineData("confidence", AiRiskRejectionReason.LowConfidence)]
    [InlineData("cash", AiRiskRejectionReason.InsufficientCash)]
    [InlineData("positions", AiRiskRejectionReason.MaxPositionsReached)]
    [InlineData("exposure", AiRiskRejectionReason.MaxSymbolExposureReached)]
    public async Task Revalidation_rejects_approvals_that_current_policy_or_state_no_longer_supports(string field, AiRiskRejectionReason reason)
    {
        await using var f = await Fixture.CreateAsync();
        var positions = field == "positions" ? Enumerable.Range(0, 10).Select(i => ($"S{i}", 1m, 100m)).ToArray()
            : field == "exposure" ? new[] { ("AAPL", 200m, 100m) }
            : field == "cash" ? new[] { ("OTHER", 100m, 100m) } : [];
        await f.SeedAsync(field == "cash" ? 0m : field == "exposure" ? 80000m : 100000m, positions);
        var error = await Failure(f.Engine(field == "confidence" ? new() { MinimumConfidence = 0.9m } : null), await f.RequestAsync(), AiTradeExecutionFailure.RiskChanged);
        Assert.Equal(reason, error.RiskReason);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTrades.ToListAsync());
        Assert.Equal(positions.Length, await db.AiTraderPositions.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Stale_sell_never_sells_missing_or_insufficient_shares(int held)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(100000m, held == 0 ? [] : [("AAPL", held, 100m)]);
        await Failure(f.Engine(), await f.RequestAsync(AiTradingSignal.Sell, 10m), AiTradeExecutionFailure.InsufficientHoldings);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTrades.ToListAsync());
        Assert.Equal(100000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("quantity")]
    [InlineData("new-position")]
    public async Task State_changed_during_market_fetch_aborts_before_mutation(string change)
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(99000m, ("AAPL", 10m, 100m));
        f.Market.BeforeQuote = async () =>
        {
            await using var writer = f.CreateDbContext();
            if (change == "cash") (await writer.AiTraderPortfolios.SingleAsync()).CashBalance = 98000m;
            else if (change == "quantity") (await writer.AiTraderPositions.SingleAsync()).Quantity = 5m;
            else writer.AiTraderPositions.Add(Position((await writer.AiTraderPortfolios.SingleAsync()).Id, "MSFT", 1m, 100m));
            await writer.SaveChangesAsync();
        };
        await Failure(f.Engine(), await f.RequestAsync(), AiTradeExecutionFailure.ConcurrencyConflict);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTrades.ToListAsync());
        Assert.Equal(change == "cash" ? 98000m : 99000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [Fact]
    public async Task Save_failure_after_sql_writes_rolls_back_cash_position_and_trade()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        await Failure(f.Engine(interceptor: new FailAfterSave()), await f.RequestAsync(), AiTradeExecutionFailure.PersistenceFailure);
        await f.AssertUnchangedAsync();
    }

    [Fact]
    public async Task Quantity_is_floored_price_and_total_are_rounded_away_from_zero()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        f.Market.Price = 1.23445m;
        var result = await f.Engine().ExecuteAsync(await f.RequestAsync(quantity: 1.234567899m));
        Assert.Equal(1.23456789m, result.ExecutedQuantity);
        Assert.Equal(1.2345m, result.ExecutionPrice);
        Assert.Equal(1.5241m, result.TotalAmount);
        Assert.Equal(99998.4759m, result.CashBalance);
    }

    [Fact]
    public async Task Cash_and_storage_limits_remain_safe_at_precision_boundaries()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(0.0001m);
        f.Market.Price = 0.0001m;
        var result = await f.Engine(new() { MaxCashAllocationPerTradePercent = 1m, MaxPositionExposurePercent = 1m })
            .ExecuteAsync(await f.RequestAsync(quantity: 1m));
        Assert.Equal(0m, result.CashBalance);
        Assert.Equal(0.0001m, result.TotalAmount);
    }

    [Fact]
    public async Task Rounded_settlement_cannot_exceed_allocation_or_reduce_total_value_below_exposure_limit()
    {
        await using var f = await Fixture.CreateAsync();
        // Risk budget is 0.00015: storing a rounded 0.0002 debit would exceed it.
        await f.SeedAsync(0.0015m);
        f.Market.Price = 0.0001m;
        await Failure(f.Engine(), await f.RequestAsync(quantity: 1.5m), AiTradeExecutionFailure.RiskChanged);
        await using var db = f.CreateDbContext();
        Assert.Equal(0.0015m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Empty(await db.AiTrades.ToListAsync());
    }

    [Fact]
    public async Task Rounding_debit_up_cannot_increase_symbol_exposure_above_limit()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(0.0011m, ("AAPL", 1.6m, 0.0001m));
        f.Market.Price = 0.0001m;
        // Safe raw size .92 shares, cost .000092 -> .0001. Exposure .000252
        // would exceed .2 * (.00126 - .0001 + .000092) = .0002504.
        await Failure(f.Engine(), await f.RequestAsync(quantity: 1m), AiTradeExecutionFailure.RiskChanged);
        await using var db = f.CreateDbContext();
        Assert.Equal(0.0011m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Equal(1.6m, (await db.AiTraderPositions.SingleAsync()).Quantity);
        Assert.Empty(await db.AiTrades.ToListAsync());
    }

    [Fact]
    public async Task Settlement_rounding_cannot_push_another_symbol_across_its_exposure_limit()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(80000m, ("MSFT", 200m, 100m));
        f.Market.Quote = symbol => Quote(symbol, symbol == "AAPL" ? 1.2346m : 100m);
        await Failure(f.Engine(), await f.RequestAsync(quantity: 0.99999m), AiTradeExecutionFailure.RiskChanged);
        await using var db = f.CreateDbContext();
        Assert.Equal(80000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Empty(await db.AiTrades.ToListAsync());
    }

    [Fact]
    public async Task Ai_execution_does_not_modify_existing_user_rows_or_save_scoped_edits()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        await using var db = f.CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Test", Email = "isolation@example.com",
            NormalizedEmail = "ISOLATION@EXAMPLE.COM", PasswordHash = "test-hash", CreatedAtUtc = Fixture.Before,
            UpdatedAtUtc = Fixture.Before };
        var portfolio = new Portfolio { Id = Guid.NewGuid(), User = user, CashBalance = 90000m, CreatedAtUtc = Fixture.Before };
        var holding = new Holding { Id = Guid.NewGuid(), Portfolio = portfolio, Symbol = "AAPL", Quantity = 100m,
            AverageCost = 100m, UpdatedAtUtc = Fixture.Before };
        var trade = new Transaction { Id = Guid.NewGuid(), Portfolio = portfolio, OrderId = Guid.NewGuid(), Side = "BUY",
            Symbol = "AAPL", Quantity = 100m, ExecutionPrice = 100m, TotalAmount = 10000m, ExecutedAtUtc = Fixture.Before };
        db.AddRange(holding, trade);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        async Task<string> UserState() => System.Text.Json.JsonSerializer.Serialize(new
        {
            Users = await db.Users.AsNoTracking().ToArrayAsync(), Portfolios = await db.Portfolios.AsNoTracking().ToArrayAsync(),
            Holdings = await db.Holdings.AsNoTracking().ToArrayAsync(), Trades = await db.Transactions.AsNoTracking().ToArrayAsync()
        });
        var before = await UserState();
        (await db.Users.SingleAsync()).DisplayName = "Pending unrelated edit";
        await f.Engine().ExecuteAsync(await f.RequestAsync());
        await f.Engine().ExecuteAsync(await f.RequestAsync(AiTradingSignal.Sell));
        Assert.Equal(before, await UserState());
        Assert.Equal(EntityState.Modified, db.Entry(await db.Users.SingleAsync()).State);
        Assert.Equal(2, await db.AiTrades.CountAsync());
    }

    [Fact]
    public async Task Buy_needs_valid_other_position_quotes_but_sell_needs_only_target_quote()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync(98000m, ("AAPL", 10m, 100m), ("OTHER", 10m, 100m));
        f.Market.Quote = symbol => symbol == "AAPL" ? Quote(symbol, 100m) : null;
        await Failure(f.Engine(), await f.RequestAsync(), AiTradeExecutionFailure.QuoteUnavailable);
        var sell = await f.Engine().ExecuteAsync(await f.RequestAsync(AiTradingSignal.Sell, 10m));
        Assert.Equal(99000m, sell.CashBalance);
        await using var db = f.CreateDbContext();
        Assert.Equal("OTHER", (await db.AiTraderPositions.SingleAsync()).Symbol);
        Assert.Equal("SELL", (await db.AiTrades.SingleAsync()).Side);
    }

    [Fact]
    public async Task Cancellation_propagates_without_any_execution()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SeedAsync();
        using var cts = new CancellationTokenSource();
        f.Market.BeforeQuote = () => { cts.Cancel(); return Task.CompletedTask; };
        var request = await f.RequestAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Engine().ExecuteAsync(request, cts.Token));
        await f.AssertUnchangedAsync();
    }

    internal static AiTradeExecutionRequest Request(AiTradingSignal signal = AiTradingSignal.Buy, decimal quantity = 100m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new(true, "AAPL", signal, 0.8m, 100m, quantity, null));
    internal static StockQuote Quote(string symbol, decimal price) => new(symbol, "USD", price, null, null, null, Fixture.Now);
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    internal static async Task<AiTradeExecutionException> Failure(IAiTradeExecutionService engine, AiTradeExecutionRequest request, AiTradeExecutionFailure category)
    {
        var error = await Assert.ThrowsAsync<AiTradeExecutionException>(() => engine.ExecuteAsync(request));
        Assert.Equal(category, error.Category);
        Assert.Null(error.InnerException);
        return error;
    }
    internal static AiTraderPosition Position(Guid portfolioId, string symbol, decimal quantity, decimal cost) => new()
    {
        Id = Guid.NewGuid(), AiTraderPortfolioId = portfolioId, Symbol = symbol, Quantity = quantity, AverageCost = cost,
        CreatedAtUtc = Fixture.Before, UpdatedAtUtc = Fixture.Before
    };

    internal sealed class Fixture(SqliteConnection connection) : IDbContextFactory<StockLabDbContext>, IAsyncDisposable
    {
        internal static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        internal static readonly DateTime Before = Now.AddDays(-1).UtcDateTime;
        public FakeMarket Market { get; } = new();
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
        public StockLabDbContext CreateDbContext() => new SqliteContext(Options());
        private DbContextOptions<StockLabDbContext> Options(IInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<StockLabDbContext>().UseSqlite(connection);
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            return builder.Options;
        }
        public IAiTradeExecutionService Engine(AiRiskOptions? policy = null, IInterceptor? interceptor = null)
        {
            return new AiPaperTradingEngine(new SqliteFactory(Options(interceptor)), Market,
                Microsoft.Extensions.Options.Options.Create(policy ?? new()), new FixedClock());
        }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var f = new Fixture(connection);
            await using var db = f.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            return f;
        }
        public async Task SeedAsync(decimal cash = 100000m, params (string Symbol, decimal Quantity, decimal Cost)[] positions)
        {
            await using var db = CreateDbContext();
            var portfolio = new AiTraderPortfolio { Id = Guid.NewGuid(), CashBalance = cash, CreatedAtUtc = Before, UpdatedAtUtc = Before };
            db.AiTraderPortfolios.Add(portfolio);
            db.AiTraderPositions.AddRange(positions.Select(p => Position(portfolio.Id, p.Symbol, p.Quantity, p.Cost)));
            await db.SaveChangesAsync();
        }
        public async Task AssertUnchangedAsync()
        {
            await using var db = CreateDbContext();
            var p = await db.AiTraderPortfolios.SingleAsync();
            Assert.Equal(100000m, p.CashBalance);
            Assert.Equal(Before, p.UpdatedAtUtc);
            Assert.Empty(await db.AiTraderPositions.ToListAsync());
            Assert.Empty(await db.AiTrades.ToListAsync());
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
    private sealed class SqliteContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AiTraderPortfolio>().Property(p => p.Version).HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<User>().Property(p => p.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(p => p.Version).ValueGeneratedNever();
        }
    }
    private sealed class SqliteFactory(DbContextOptions<StockLabDbContext> options) : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => new SqliteContext(options);
    }
    internal sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Fixture.Now;
    }
    internal sealed class FakeMarket : IMarketDataProvider
    {
        public decimal Price { get; set; } = 100m;
        public Func<string, StockQuote?>? Quote { get; set; }
        public Func<Task>? BeforeQuote { get; set; }
        public List<string> Calls { get; } = [];
        public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            Calls.Add(symbol);
            if (BeforeQuote is not null) { var hook = BeforeQuote; BeforeQuote = null; await hook(); }
            cancellationToken.ThrowIfCancellationRequested();
            return Quote is null ? AiPaperTradingEngineTests.Quote(symbol, Price) : Quote(symbol);
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Private database details after executing SQL");
    }
}
