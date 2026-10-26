using System.Data.Common;
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

public sealed class AiRiskExecutionTests
{
    [Fact]
    public async Task Buy_and_sell_use_real_risk_and_engine_without_touching_user_portfolios()
    {
        await using var f = await Fixture();
        await using (var seed = f.CreateDbContext())
        {
            seed.Portfolios.Add(new Portfolio { Id = Guid.NewGuid(), CashBalance = 12345m,
                User = new User { Id = Guid.NewGuid(), DisplayName = "Test", Email = "isolation@example.test",
                    NormalizedEmail = "ISOLATION@EXAMPLE.TEST", PasswordHash = "test-only" },
                Holdings = [new Holding { Id = Guid.NewGuid(), Symbol = "AAPL", Quantity = 3, AverageCost = 50 }] });
            await seed.SaveChangesAsync();
        }
        var service = Service(f);
        var buy = await Record(f, AiTradingSignal.Buy);
        var bought = await service.ExecuteAsync(buy.Id);
        Assert.Equal("EXECUTED", bought.Status);
        Assert.False(bought.IsReplay);
        f.ExecutionMarket.Prices["AAPL"] = 120;
        var sell = await Record(f, AiTradingSignal.Sell);
        Assert.Equal("EXECUTED", (await service.ExecuteAsync(sell.Id)).Status);
        await using var db = f.CreateDbContext();
        Assert.Equal(102000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
        Assert.Empty(await db.AiTraderPositions.ToArrayAsync());
        Assert.Equal(new[] { "BUY", "SELL" }, (await db.AiTrades.OrderBy(t => t.Side).ToArrayAsync()).Select(t => t.Side));
        var attempts = await db.AiExecutionAttempts.ToArrayAsync();
        Assert.Equal(2, attempts.Length);
        Assert.All(attempts, a => { Assert.Equal("EXECUTED", a.Status); Assert.NotNull(a.TradeId); Assert.NotNull(a.CompletedAtUtc); });
        Assert.Equal(12345m, (await db.Portfolios.SingleAsync()).CashBalance);
        Assert.Equal(3m, (await db.Holdings.SingleAsync()).Quantity);
        Assert.Empty(await db.Transactions.ToArrayAsync());
    }

    [Fact]
    public async Task Hold_and_terminal_replays_never_fetch_quotes_or_submit_another_order()
    {
        await using var f = await Fixture();
        var service = Service(f);
        var hold = await Record(f, AiTradingSignal.Hold);
        Assert.Equal("HOLD", (await service.ExecuteAsync(hold.Id)).Status);
        Assert.Empty(f.ExecutionMarket.Calls);
        var buy = await Record(f, AiTradingSignal.Buy);
        var original = await service.ExecuteAsync(buy.Id);
        var calls = f.ExecutionMarket.Calls.Count;
        f.ExecutionMarket.Quote = (_, _) => throw new InvalidOperationException("must not quote on replay");
        var replay = await service.ExecuteAsync(buy.Id);
        Assert.True(replay.IsReplay);
        Assert.Equal(original.TradeId, replay.TradeId);
        Assert.Equal(calls, f.ExecutionMarket.Calls.Count);
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiTrades.ToArrayAsync());
        Assert.Single(await db.AiExecutionAttempts.ToArrayAsync());
    }

    [Theory]
    [InlineData(AiTradingSignal.Buy, "0.6", "LowConfidence")]
    [InlineData(AiTradingSignal.Sell, "0.9", "NoPositionToSell")]
    public async Task Business_refusals_are_persisted_and_not_reconsidered_on_retry(AiTradingSignal signal, string confidence, string reason)
    {
        await using var f = await Fixture();
        var raw = await Record(f, signal, decimal.Parse(confidence, System.Globalization.CultureInfo.InvariantCulture));
        var service = Service(f);
        var result = await service.ExecuteAsync(raw.Id);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(reason, result.FailureCode);
        f.ExecutionMarket.Quote = (_, _) => throw new InvalidOperationException("must not retry a terminal refusal");
        Assert.True((await service.ExecuteAsync(raw.Id)).IsReplay);
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiRejectedDecisions.ToArrayAsync());
        Assert.Empty(await db.AiTrades.ToArrayAsync());
        Assert.Equal("REJECTED", (await db.AiExecutionAttempts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Execution_reduces_approved_quantity_when_the_fresh_price_increases()
    {
        await using var f = await Fixture();
        f.ExecutionMarket.Quote = (s, _) => new(s, "USD", f.ExecutionMarket.Calls.Count == 1 ? 100m : 200m,
            null, null, null, AiCurrentPositionsFixture.Now);
        var decision = await Record(f, AiTradingSignal.Buy);
        Assert.Equal("EXECUTED", (await Service(f).ExecuteAsync(decision.Id)).Status);
        await using var db = f.CreateDbContext();
        var trade = await db.AiTrades.SingleAsync();
        var attempt = await db.AiExecutionAttempts.SingleAsync();
        Assert.Equal(decision.Id, trade.OrderId);
        Assert.Equal(100m, attempt.ApprovedQuantity);
        Assert.Equal(100m, attempt.RequestedPrice);
        Assert.Equal(50m, trade.Quantity);
        Assert.Equal(200m, trade.ExecutionPrice);
        Assert.Equal(90000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [Fact]
    public async Task Changed_risk_policy_at_settlement_is_persisted_as_a_business_refusal()
    {
        await using var f = await Fixture();
        var policy = new AiRiskOptions();
        f.ExecutionMarket.Quote = (s, _) =>
        {
            if (f.ExecutionMarket.Calls.Count == 2) policy.MinimumConfidence = .99m;
            return new(s, "USD", 100, null, null, null, AiCurrentPositionsFixture.Now);
        };
        var result = await Service(f, policy).ExecuteAsync((await Record(f, AiTradingSignal.Buy)).Id);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal("LowConfidence", result.FailureCode);
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiRejectedDecisions.ToArrayAsync());
        Assert.Empty(await db.AiTrades.ToArrayAsync());
        Assert.Equal(100000m, (await db.AiTraderPortfolios.SingleAsync()).CashBalance);
    }

    [Theory]
    [InlineData(false, "MaxSymbolExposureReached")]
    [InlineData(true, "MaxPositionsReached")]
    public async Task Existing_exposure_and_position_limits_block_additional_orders(bool positions, string reason)
    {
        await using var f = await Fixture();
        var policy = new AiRiskOptions { MaxOpenPositions = positions ? 1 : 10,
            MaxPositionExposurePercent = positions ? .2m : .1m };
        var service = Service(f, policy);
        await service.ExecuteAsync((await Record(f, AiTradingSignal.Buy)).Id);
        f.ExecutionMarket.Prices["MSFT"] = f.ValuationMarket.Prices["MSFT"] = 100m;
        var next = await Record(f, AiTradingSignal.Buy, symbol: positions ? "MSFT" : "AAPL");
        var result = await service.ExecuteAsync(next.Id);
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(reason, result.FailureCode);
        await using var db = f.CreateDbContext();
        Assert.Single(await db.AiTrades.ToArrayAsync());
        Assert.Single(await db.AiRejectedDecisions.ToArrayAsync());
    }

    [Theory]
    [InlineData("missing", "QuoteUnavailable")]
    [InlineData("currency", "CurrencyMismatch")]
    [InlineData("symbol", "InvalidExecutionPrice")]
    [InlineData("price", "InvalidExecutionPrice")]
    public async Task Quote_failures_are_durable_retryable_attempts_not_permanent_risk_refusals(string invalid, string code)
    {
        await using var f = await Fixture();
        f.ExecutionMarket.Quote = (s, _) => invalid == "missing" ? null : new(invalid == "symbol" ? "OTHER" : s,
            invalid == "currency" ? "CAD" : "USD", invalid == "price" ? 0 : 100, null, null, null, AiCurrentPositionsFixture.Now);
        var decision = await Record(f, AiTradingSignal.Buy);
        var service = Service(f);
        var result = await service.ExecuteAsync(decision.Id);
        Assert.Equal("FAILED", result.Status);
        Assert.Equal(code, result.FailureCode);
        await using (var db = f.CreateDbContext())
        {
            Assert.Empty(await db.AiRejectedDecisions.ToArrayAsync());
            Assert.Empty(await db.AiTrades.ToArrayAsync());
            Assert.Equal(code, (await db.AiExecutionAttempts.SingleAsync()).FailureCode);
        }
        f.ExecutionMarket.Quote = null;
        Assert.Equal("EXECUTED", (await service.ExecuteAsync(decision.Id)).Status);
        await using var reader = f.CreateDbContext();
        Assert.Equal(2, await reader.AiExecutionAttempts.CountAsync());
        Assert.Single(await reader.AiTrades.ToArrayAsync());
    }

    [Fact]
    public async Task Caller_cancellation_is_journaled_with_an_independent_token_and_can_be_retried()
    {
        await using var f = await Fixture();
        using var cancellation = new CancellationTokenSource();
        f.ExecutionMarket.Quote = (_, token) => { cancellation.Cancel(); throw new OperationCanceledException(token); };
        var raw = await Record(f, AiTradingSignal.Buy);
        var service = Service(f);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(raw.Id, cancellation.Token));
        await using (var db = f.CreateDbContext())
        {
            Assert.Equal("CANCELLED", (await db.AiExecutionAttempts.SingleAsync()).Status);
            Assert.Empty(await db.AiTrades.ToArrayAsync());
        }
        f.ExecutionMarket.Quote = null;
        Assert.Equal("EXECUTED", (await service.ExecuteAsync(raw.Id)).Status);
    }

    [Fact]
    public async Task A_committed_trade_with_unfinished_audit_is_reconciled_without_another_quote_or_trade()
    {
        await using var f = await Fixture();
        var blocker = new BlockCompletion();
        var raw = await Record(f, AiTradingSignal.Buy);
        var service = Service(f, journal: f.With(blocker));
        var error = await Assert.ThrowsAsync<AiTradeExecutionException>(() => service.ExecuteAsync(raw.Id));
        Assert.Equal(AiTradeExecutionFailure.PersistenceFailure, error.Category);
        await using (var db = f.CreateDbContext())
        {
            Assert.Single(await db.AiTrades.ToArrayAsync());
            Assert.Equal("STARTED", (await db.AiExecutionAttempts.SingleAsync()).Status);
        }
        blocker.Enabled = false;
        var calls = f.ExecutionMarket.Calls.Count;
        f.ExecutionMarket.Quote = (_, _) => throw new InvalidOperationException("must not repeat a committed order");
        var replay = await service.ExecuteAsync(raw.Id);
        Assert.True(replay.IsReplay);
        Assert.Equal(calls, f.ExecutionMarket.Calls.Count);
        await using var reader = f.CreateDbContext();
        var recovered = await reader.AiExecutionAttempts.SingleAsync();
        Assert.Equal("EXECUTED", recovered.Status);
        Assert.True(recovered.IsReplay);
        Assert.NotNull(recovered.CompletedAtUtc);
    }

    [Fact]
    public async Task Missing_or_empty_decision_ids_do_not_start_an_attempt()
    {
        await using var f = await Fixture();
        var service = Service(f);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExecuteAsync(Guid.Empty));
        var error = await Assert.ThrowsAsync<AiTradeExecutionException>(() => service.ExecuteAsync(Guid.NewGuid()));
        Assert.Equal(AiTradeExecutionFailure.DecisionNotFound, error.Category);
        Assert.Empty(f.ExecutionMarket.Calls);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiExecutionAttempts.ToArrayAsync());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("symbol")]
    [InlineData("confidence")]
    [InlineData("price")]
    [InlineData("quantity")]
    [InlineData("precision")]
    [InlineData("reason")]
    public async Task Malformed_risk_output_is_failed_before_any_order_or_refusal_is_written(string invalid)
    {
        await using var f = await Fixture();
        var raw = await Record(f, AiTradingSignal.Buy);
        var response = new AiRiskDecision(true, "AAPL", AiTradingSignal.Buy, .9m, 100m, 100m, null);
        response = invalid switch
        {
            "null" => null!, "symbol" => response with { Symbol = "MSFT" },
            "confidence" => response with { Confidence = .8m }, "price" => response with { RequestedPrice = 90m },
            "quantity" => response with { ApprovedQuantity = 0m }, "precision" => response with { ApprovedQuantity = 1.000000001m },
            _ => response with { Approved = false, ApprovedQuantity = 0, RejectionReason = (AiRiskRejectionReason)99 }
        };
        var result = await Service(f, risk: new SuppliedRisk(response)).ExecuteAsync(raw.Id);
        Assert.Equal("FAILED", result.Status);
        Assert.Equal("InvalidRiskDecision", result.FailureCode);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTrades.ToArrayAsync());
        Assert.Empty(await db.AiRejectedDecisions.ToArrayAsync());
        Assert.Null((await db.AiExecutionAttempts.SingleAsync()).ApprovedQuantity);
    }

    [Fact]
    public async Task A_journal_start_failure_prevents_quotes_and_execution()
    {
        await using var f = await Fixture();
        var raw = await Record(f, AiTradingSignal.Buy);
        var error = await Assert.ThrowsAsync<AiTradeExecutionException>(() =>
            Service(f, journal: f.With(new BlockStart())).ExecuteAsync(raw.Id));
        Assert.Equal(AiTradeExecutionFailure.PersistenceFailure, error.Category);
        Assert.Empty(f.ExecutionMarket.Calls);
        await using var db = f.CreateDbContext();
        Assert.Empty(await db.AiTrades.ToArrayAsync());
        Assert.Empty(await db.AiExecutionAttempts.ToArrayAsync());
    }

    internal static async Task<AiCurrentPositionsFixture> Fixture(bool sql = false)
    {
        var f = await AiCurrentPositionsFixture.CreateAsync(sql);
        f.ExecutionMarket.Prices["AAPL"] = f.ValuationMarket.Prices["AAPL"] = 100m;
        await f.InitializeAsync();
        return f;
    }

    internal static Task<AiDecisionRecord> Record(AiCurrentPositionsFixture f, AiTradingSignal signal, decimal confidence = .9m, string symbol = "AAPL") =>
        new AiDecisionHistoryService(f, new AiCurrentPositionsFixture.Clock()).RecordAsync(new(Guid.NewGuid(), symbol, signal,
            confidence, new(2026, 10, 2), "test-model", "test-version"));

    internal static AiRiskExecutionService Service(AiCurrentPositionsFixture f, AiRiskOptions? policy = null,
        IDbContextFactory<StockLabDbContext>? journal = null, IAiRiskManager? risk = null, IAiTradeExecutionService? execution = null,
        IMarketDataProvider? quotes = null)
    {
        var options = Options.Create(policy ?? new AiRiskOptions());
        var clock = new AiCurrentPositionsFixture.Clock();
        return new(journal ?? f, new AiDecisionHistoryService(f, clock), new AiDecisionOutcomeReadService(f),
            risk ?? new AiRiskManager(new AiTraderPortfolioService(f, quotes ?? f.ValuationMarket, clock), options),
            execution ?? new AiPaperTradingEngine(f, quotes ?? f.ExecutionMarket, options, clock),
            new AiRejectedDecisionHistoryService(f, clock), quotes ?? f.ExecutionMarket, clock);
    }

    private sealed class SuppliedRisk(AiRiskDecision response) : IAiRiskManager
    {
        public Task<AiRiskDecision> EvaluateAsync(AiRiskRequest request, CancellationToken cancellationToken = default) => Task.FromResult(response);
    }

    private sealed class BlockStart : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("test-only journal start failure");
    }

    private sealed class BlockCompletion : DbCommandInterceptor
    {
        public bool Enabled { get; set; } = true;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("AiExecutionAttempts")
                && command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, "EXECUTED")))
                throw new DbUpdateException("test-only completion failure");
            return new(result);
        }
    }
}
