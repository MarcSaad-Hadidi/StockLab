using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Domain.Entities;
using static StockLab.UnitTests.Api.AiTraderApiTests;

namespace StockLab.UnitTests.Api;

public sealed class AiTraderApiHistoryTests
{
    private const string Root = "/api/ai-trader";

    [Fact]
    public async Task Equity_history_uses_default_twenty_and_never_reads_more_than_two_hundred_observations()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        var state = await f.InitializeAsync();
        var times = new List<DateTime>();
        await using (var db = f.Database.CreateDbContext())
        {
            // Explicit test-only stored observations exercise paging independently of market valuation.
            for (var i = 0; i < 205; i++)
            {
                f.Time.Advance();
                times.Add(f.Time.Now.UtcDateTime);
                db.AiTraderValuationSnapshots.Add(new AiTraderValuationSnapshot
                {
                    Id = Guid.NewGuid(), AiTraderPortfolioId = state.PortfolioId, CaptureId = Guid.NewGuid(),
                    ValuedAtUtc = f.Time.Now.UtcDateTime, CashBalance = 100000m, PositionsValue = i,
                    PortfolioVersion = state.Version.ToArray()
                });
            }
            await db.SaveChangesAsync();
        }
        var before = await f.Database.StateAsync();
        foreach (var (query, expected) in new[] { ("", 20), ("?limit=1", 1), ("?limit=200", 200) })
        {
            f.Observer.Reset();
            var rows = (await f.ReadAsync($"{Root}/performance/history{query}", AiTraderApiFixture.Token()))
                .EnumerateArray().ToArray();
            Assert.Equal(times.TakeLast(expected), rows.Select(p => p.GetProperty("valuedAtUtc").GetDateTime().ToUniversalTime()));
            Assert.InRange(f.Observer.Commands.Count, 1, 2);
            Assert.Contains(f.Observer.Commands, command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, f.Observer.SaveCalls);
        }
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Empty(f.Market.Calls);
    }

    [Fact]
    public async Task Persisted_equity_history_is_chronological_filtered_bounded_and_unchanged_by_new_quotes()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        f.Market.Prices["AAPL"] = 130m;
        var high = await f.CaptureAsync();
        f.Market.Prices["AAPL"] = 80m;
        var low = await f.CaptureAsync();
        f.Market.Prices["AAPL"] = 999m;
        f.Market.Calls.Clear();
        f.Observer.Reset();
        var before = await f.Database.StateAsync();
        var token = AiTraderApiFixture.Token();
        var all = (await f.ReadAsync($"{Root}/performance/history?limit=200", token)).EnumerateArray().ToArray();
        Assert.Equal(3, all.Length);
        Assert.True(all[0].GetProperty("isInitialBaseline").GetBoolean());
        AssertMoney(all[1], "cashBalance", high.CashBalance);
        AssertMoney(all[1], "positionsValue", high.PositionsValue);
        AssertMoney(all[1], "totalValue", 100300m);
        AssertMoney(all[1], "pnL", 300m);
        AssertFraction(all[1], "return", .003m);
        AssertMoney(all[2], "totalValue", 99800m);
        AssertMoney(all[2], "pnL", -200m);
        AssertFraction(all[2], "return", -.002m);
        Assert.All(all, point => AssertUtc(point, "valuedAtUtc"));
        Assert.All(all, point => Assert.False(point.TryGetProperty("quoteObservationsJson", out _)));
        var last = Assert.Single((await f.ReadAsync($"{Root}/performance/history?limit=1", token)).EnumerateArray());
        AssertMoney(last, "totalValue", low.TotalValue);
        var start = Uri.EscapeDataString(high.ValuedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        var end = Uri.EscapeDataString(high.ValuedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        var filtered = Assert.Single((await f.ReadAsync(
            $"{Root}/performance/history?startDateUtc={start}&endDateUtc={end}", token)).EnumerateArray());
        AssertMoney(filtered, "totalValue", high.TotalValue);
        var offsetZero = Uri.EscapeDataString(new DateTimeOffset(high.ValuedAtUtc).ToString("O", CultureInfo.InvariantCulture));
        Assert.Single((await f.ReadAsync($"{Root}/performance/history?startDateUtc={offsetZero}&endDateUtc={offsetZero}", token)).EnumerateArray());
        var future = Uri.EscapeDataString(f.Time.Now.AddDays(1).ToString("O", CultureInfo.InvariantCulture));
        Assert.Empty((await f.ReadAsync($"{Root}/performance/history?startDateUtc={future}", token)).EnumerateArray());
        Assert.Empty(f.Market.Calls);
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Contains(f.Observer.Commands, command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Absent_valuation_observations_remain_empty_with_nullable_drawdown_and_honest_coverage()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.InitializeAsync();
        await using (var db = f.Database.CreateDbContext())
        {
            await db.AiTraderValuationSnapshots.ExecuteDeleteAsync();
        }
        var before = await f.Database.StateAsync();
        f.Observer.Reset();
        var token = AiTraderApiFixture.Token();
        Assert.Empty((await f.ReadAsync($"{Root}/performance/history", token)).EnumerateArray());
        var metrics = await f.ReadAsync($"{Root}/performance", token);
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("maxDrawdown").ValueKind);
        var coverage = metrics.GetProperty("historicalCoverage");
        Assert.Equal(0, coverage.GetProperty("valuationSampleCount").GetInt32());
        Assert.Equal("Absent", coverage.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("valuationFirstAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("valuationLastAtUtc").ValueKind);
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    [Fact]
    public async Task Legacy_unlinked_BUY_and_unknown_SELL_preserve_nulls_in_details_and_performance()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.InitializeAsync();
        var buy = await f.ExecuteAsync();
        var sell = await f.ExecuteAsync(AiTradingSignal.Sell, price: 120m);
        await using (var db = f.Database.CreateDbContext())
        {
            foreach (var row in await db.AiTrades.ToArrayAsync())
            {
                row.AiDecisionId = null;
                row.RealizedPnL = null;
            }
            await db.SaveChangesAsync();
        }
        f.Market.Quote = (_, _) => throw new InvalidOperationException("Historical routes must not request quotes.");
        var before = await f.Database.StateAsync();
        f.Observer.Reset();
        f.Market.Calls.Clear();
        var token = AiTraderApiFixture.Token();
        foreach (var id in new[] { buy.TradeId, sell.TradeId })
        {
            var detail = await f.ReadAsync($"{Root}/trades/{id}", token);
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("decisionId").ValueKind);
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("decision").ValueKind);
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("realizedPnL").ValueKind);
        }
        Assert.Equal(2, (await f.ReadAsync($"{Root}/trades", token)).GetArrayLength());
        var performance = await f.ReadAsync($"{Root}/performance", token);
        AssertMoney(performance, "totalValue", 100200m);
        AssertMoney(performance, "totalPnL", 200m);
        Assert.False(performance.GetProperty("tradeStatisticsComplete").GetBoolean());
        Assert.Equal(1, performance.GetProperty("unvaluedLegacySellCount").GetInt32());
        foreach (var name in new[] { "realizedPnL", "winRate", "averageWin", "averageLoss", "bestTrade", "worstTrade" })
            Assert.Equal(JsonValueKind.Null, performance.GetProperty(name).ValueKind);
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Empty(f.Market.Calls);
    }

    [Fact]
    public async Task Trade_history_default_and_explicit_limits_are_applied_by_one_bounded_SQL_read()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.InitializeAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 25; i++) ids.Add((await f.ExecuteAsync(quantity: 1m, price: 1m)).TradeId);
        ids.Reverse();
        f.Market.Calls.Clear();
        var before = await f.Database.StateAsync();
        foreach (var (query, expected) in new[] { ("", 20), ("?limit=1", 1), ("?limit=200", 25) })
        {
            f.Observer.Reset();
            var rows = (await f.ReadAsync($"{Root}/trades{query}", AiTraderApiFixture.Token())).EnumerateArray().ToArray();
            Assert.Equal(ids.Take(expected), rows.Select(t => t.GetProperty("tradeId").GetGuid()));
            var command = Assert.Single(f.Observer.Commands);
            Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LEFT JOIN", command, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, f.Observer.SaveCalls);
        }
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Empty(f.Market.Calls);
    }

    [Fact]
    public async Task Decisions_use_bounded_SQL_and_exact_outcomes_even_when_rejection_is_outside_recent_rejection_page()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        var selected = Guid.NewGuid();
        var ids = new List<Guid>();
        await using (var db = f.Database.CreateDbContext())
        {
            db.AiDecisions.Add(new AiDecision
            {
                Id = selected, Symbol = "AAPL", Signal = "BUY", Confidence = .1m,
                DecisionDate = new(2026, 10, 2), ModelName = "test-only-model", ModelVersion = "original-version",
                RecordedAtUtc = f.Time.Now.AddDays(-2).UtcDateTime
            });
            db.AiRejectedDecisions.Add(new AiRejectedDecision
            {
                AiDecisionId = selected, RejectionReason = "LowConfidence", RejectedAtUtc = f.Time.Now.AddDays(-1).UtcDateTime
            });
            for (var i = 0; i < 205; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                db.AiDecisions.Add(new AiDecision
                {
                    Id = id, Symbol = "MSFT", Signal = "SELL", Confidence = .9m,
                    DecisionDate = new(2026, 10, 1), ModelName = "test-only-model", ModelVersion = "older-version",
                    RecordedAtUtc = f.Time.Now.UtcDateTime.AddSeconds(i)
                });
                db.AiRejectedDecisions.Add(new AiRejectedDecision
                {
                    AiDecisionId = id, RejectionReason = "NoPositionToSell", RejectedAtUtc = f.Time.Now.UtcDateTime.AddSeconds(i)
                });
            }
            await db.SaveChangesAsync();
        }
        ids.Reverse();
        var token = AiTraderApiFixture.Token();
        var before = await f.Database.StateAsync();
        foreach (var (query, expected) in new[] { ("", 20), ("?limit=1", 1), ("?limit=200", 200) })
        {
            f.Observer.Reset();
            var rows = (await f.ReadAsync($"{Root}/decisions{query}", token)).EnumerateArray().ToArray();
            Assert.Equal(new[] { selected }.Concat(ids).Take(expected), rows.Select(d => d.GetProperty("decisionId").GetGuid()));
            Assert.All(rows, d => Assert.Equal("REJECTED", d.GetProperty("executionStatus").GetString()));
            var selectedRow = rows[0];
            Assert.Equal("LowConfidence", selectedRow.GetProperty("rejectionReason").GetString());
            Assert.Equal("original-version", selectedRow.GetProperty("modelVersion").GetString());
            AssertFraction(selectedRow, "confidence", .1m);
            Assert.Equal("2026-10-02", selectedRow.GetProperty("decisionDate").GetString());
            AssertUtc(selectedRow, "recordedAtUtc");
            Assert.InRange(f.Observer.Commands.Count, 1, 3);
            Assert.Contains(f.Observer.Commands, sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(f.Observer.Commands, sql => sql.Contains("LEFT JOIN", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, f.Observer.SaveCalls);
        }
        foreach (var (query, expected) in new[] { ("", 20), ("?limit=1", 1), ("?limit=200", 200) })
        {
            f.Observer.Reset();
            var rows = (await f.ReadAsync($"{Root}/rejected-decisions{query}", token)).EnumerateArray().ToArray();
            Assert.Equal(ids.Take(expected), rows.Select(d => d.GetProperty("decisionId").GetGuid()));
            var sql = Assert.Single(f.Observer.Commands);
            Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, f.Observer.SaveCalls);
        }
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Empty(f.Market.Calls);
    }

    [Fact]
    public async Task Official_HoldSignal_rejection_takes_precedence_over_unrejected_HOLD_status()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.Database.RecordDecisionAsync(AiTradingSignal.Hold, AiRiskRejectionReason.HoldSignal);
        var row = Assert.Single((await f.ReadAsync($"{Root}/decisions", AiTraderApiFixture.Token())).EnumerateArray());
        Assert.Equal("HOLD", row.GetProperty("signal").GetString());
        Assert.Equal("REJECTED", row.GetProperty("executionStatus").GetString());
        Assert.Equal("HoldSignal", row.GetProperty("rejectionReason").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("tradeId").ValueKind);
    }

    [Fact]
    public async Task Decimal_wire_values_are_invariant_and_keep_fractional_quantity_precision()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.InitializeAsync();
        var trade = await f.ExecuteAsync(quantity: 3.12500001m, price: 80.1234m);
        f.Market.Prices["AAPL"] = 81.4321m;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-CA");
            var token = AiTraderApiFixture.Token();
            var detail = await f.ReadAsync($"{Root}/trades/{trade.TradeId}", token);
            AssertMoney(detail, "quantity", 3.12500001m);
            Assert.Equal("3.12500001", detail.GetProperty("quantity").GetString());
            AssertMoney(detail, "executionPrice", 80.1234m);
            AssertMoney(detail, "totalAmount", trade.TotalAmount);
            var position = Assert.Single((await f.ReadAsync($"{Root}/positions", token)).EnumerateArray());
            AssertMoney(position, "quantity", 3.12500001m);
            AssertMoney(position, "averagePrice", 80.1234m);
            AssertMoney(position, "marketValue", 3.12500001m * 81.4321m);
            var performance = await f.ReadAsync($"{Root}/performance", token);
            AssertMoney(performance, "cashBalance", trade.CashBalance);
            Assert.Equal(JsonValueKind.Number, performance.GetProperty("totalReturn").ValueKind);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }
}
