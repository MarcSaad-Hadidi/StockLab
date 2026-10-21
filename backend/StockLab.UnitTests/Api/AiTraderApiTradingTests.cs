using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using static StockLab.UnitTests.Api.AiTraderApiTests;

namespace StockLab.UnitTests.Api;

public sealed class AiTraderApiTradingTests
{
    private const string Root = "/api/ai-trader";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Mock_provider_cannot_publish_invented_current_valuations_outside_development(string environment)
    {
        await using var f = await AiTraderApiFixture.CreateAsync(environment: environment);
        await f.InitializeAsync();
        var trade = await f.ExecuteAsync();
        await f.CaptureAsync();
        var before = await f.Database.StateAsync();
        f.Market.Calls.Clear();
        f.Observer.Reset();
        var token = AiTraderApiFixture.Token();
        foreach (var route in new[] { "overview", "positions", "performance" })
        {
            using var response = await f.GetAsync($"{Root}/{route}", token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("market_data_provider_unavailable", await ErrorAsync(response));
        }
        foreach (var route in new[] { "trades", $"trades/{trade.TradeId}", "decisions",
            $"decisions/{trade.DecisionId}", "rejected-decisions", "performance/history" })
            await f.ReadAsync($"{Root}/{route}", token);
        Assert.Empty(f.Market.Calls);
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Equal(before, await f.Database.StateAsync());
    }

    [Fact]
    public async Task Real_risk_execution_and_capture_flow_is_exposed_through_read_only_HTTP_with_user_isolation()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        var firstUser = await f.RegisterAsync("ai-first@example.com");
        var secondUser = await f.RegisterAsync("ai-second@example.com");
        var personalTrade1 = await PersonalBuyAsync(f, firstUser.Token, "MSFT");
        var personalTrade2 = await PersonalBuyAsync(f, secondUser.Token, "TSLA");
        var state = await f.InitializeAsync();

        var buy1 = await f.ExecuteAsync();
        f.Market.Prices["AAPL"] = 125m;
        await AssertPositionAsync(f, firstUser.Token, 10m, 100m, 125m, 250m);
        var buy2 = await f.ExecuteAsync(quantity: 10m, price: 120m);
        f.Market.Prices["AAPL"] = 130m;
        await AssertPositionAsync(f, firstUser.Token, 20m, 110m, 130m, 400m);
        var high = await f.CaptureAsync();
        var partial = await f.ExecuteAsync(AiTradingSignal.Sell, quantity: 4m, price: 140m);
        f.Market.Prices["AAPL"] = 100m;
        await AssertPositionAsync(f, secondUser.Token, 16m, 110m, 100m, -160m);
        var low = await f.CaptureAsync();
        var interim = await f.ReadAsync($"{Root}/performance", firstUser.Token);
        AssertMoney(interim, "cashBalance", 98360m);
        AssertMoney(interim, "positionsMarketValue", 1600m);
        AssertMoney(interim, "totalValue", 99960m);
        AssertMoney(interim, "totalPnL", -40m);
        AssertMoney(interim, "realizedPnL", 120m);
        AssertMoney(interim, "unrealizedPnL", -160m);
        AssertFraction(interim, "totalReturn", -.0004m);

        var full = await f.ExecuteAsync(AiTradingSignal.Sell, quantity: 16m, price: 105m);
        await f.CaptureAsync();
        var rejected = await f.RecordAsync(confidence: .1m);
        using (var scope = f.CreateScope())
        {
            var risk = await scope.ServiceProvider.GetRequiredService<IAiRiskManager>()
                .EvaluateAsync(new("AAPL", AiTradingSignal.Buy, .1m, 105m));
            Assert.False(risk.Approved);
            Assert.Equal(AiRiskRejectionReason.LowConfidence, risk.RejectionReason);
            await scope.ServiceProvider.GetRequiredService<IAiRejectedDecisionHistoryService>()
                .RecordAsync(new(rejected.Id, risk));
        }
        f.Time.Advance();
        var hold = await f.RecordAsync(AiTradingSignal.Hold);
        f.Time.Advance();
        var unresolved = await f.RecordAsync();

        var before = await f.Database.StateAsync();
        f.Observer.Reset();
        f.Market.Calls.Clear();
        var overview = await f.ReadAsync($"{Root}/overview", firstUser.Token);
        Assert.Equal(state.PortfolioId, overview.GetProperty("portfolioId").GetGuid());
        AssertMoney(overview, "totalValue", 100040m);
        AssertMoney(overview, "cashBalance", 100040m);
        AssertMoney(overview, "positionsMarketValue", 0m);
        AssertMoney(overview, "totalPnL", 40m);
        AssertFraction(overview, "totalReturn", .0004m);
        AssertFraction(overview, "winRate", .5m);
        Assert.Equal(4, overview.GetProperty("executedTradeCount").GetInt32());
        Assert.Equal(0, overview.GetProperty("openPositionsCount").GetInt32());
        Assert.Empty((await f.ReadAsync($"{Root}/positions", firstUser.Token)).EnumerateArray());
        var trades = (await f.ReadAsync($"{Root}/trades?limit=200", firstUser.Token)).EnumerateArray().ToArray();
        Assert.Equal(new[] { full.TradeId, partial.TradeId, buy2.TradeId, buy1.TradeId },
            trades.Select(t => t.GetProperty("tradeId").GetGuid()));
        foreach (var execution in new[] { buy1, buy2, partial, full })
        {
            var detail = await f.ReadAsync($"{Root}/trades/{execution.TradeId}", secondUser.Token);
            Assert.Equal(execution.TradeId, detail.GetProperty("tradeId").GetGuid());
            Assert.Equal(execution.OrderId, detail.GetProperty("orderId").GetGuid());
            Assert.Equal(execution.DecisionId, detail.GetProperty("decisionId").GetGuid());
            Assert.Equal(execution.Side, detail.GetProperty("side").GetString());
            Assert.Equal("AAPL", detail.GetProperty("symbol").GetString());
            AssertMoney(detail, "quantity", execution.ExecutedQuantity);
            AssertMoney(detail, "executionPrice", execution.ExecutionPrice);
            AssertMoney(detail, "totalAmount", execution.TotalAmount);
            AssertUtc(detail, "executedAtUtc");
            Assert.Equal(execution.ExecutedAtUtc, detail.GetProperty("executedAtUtc").GetDateTime().ToUniversalTime());
            if (execution.Side == "BUY") Assert.Equal(JsonValueKind.Null, detail.GetProperty("realizedPnL").ValueKind);
            else AssertMoney(detail, "realizedPnL", execution.TradeId == partial.TradeId ? 120m : -80m);
            var decision = detail.GetProperty("decision");
            Assert.Equal(execution.DecisionId, decision.GetProperty("decisionId").GetGuid());
            Assert.Equal(execution.Side, decision.GetProperty("signal").GetString());
            AssertFraction(decision, "confidence", .9m);
            Assert.Equal("2026-10-02", decision.GetProperty("decisionDate").GetString());
            Assert.Equal("test-only-model", decision.GetProperty("modelName").GetString());
            Assert.Equal("test-original-v1", decision.GetProperty("modelVersion").GetString());
        }
        var recent = Assert.Single((await f.ReadAsync($"{Root}/trades?limit=1", firstUser.Token)).EnumerateArray());
        Assert.Equal(full.TradeId, recent.GetProperty("tradeId").GetGuid());

        var decisions = (await f.ReadAsync($"{Root}/decisions?limit=200", secondUser.Token)).EnumerateArray().ToArray();
        Assert.Equal(7, decisions.Length);
        Assert.Equal(new[] { unresolved.Id, hold.Id, rejected.Id, full.DecisionId!.Value, partial.DecisionId!.Value,
            buy2.DecisionId!.Value, buy1.DecisionId!.Value }, decisions.Select(d => d.GetProperty("decisionId").GetGuid()));
        foreach (var execution in new[] { buy1, buy2, partial, full })
        {
            var decision = await f.ReadAsync($"{Root}/decisions/{execution.DecisionId}", firstUser.Token);
            Assert.Equal("EXECUTED", decision.GetProperty("executionStatus").GetString());
            Assert.Equal(execution.TradeId, decision.GetProperty("tradeId").GetGuid());
            Assert.Equal(JsonValueKind.Null, decision.GetProperty("rejectionReason").ValueKind);
        }
        var rejectedDetail = await f.ReadAsync($"{Root}/decisions/{rejected.Id}", firstUser.Token);
        Assert.Equal("REJECTED", rejectedDetail.GetProperty("executionStatus").GetString());
        Assert.Equal("LowConfidence", rejectedDetail.GetProperty("rejectionReason").GetString());
        Assert.Equal(JsonValueKind.Null, rejectedDetail.GetProperty("tradeId").ValueKind);
        var held = await f.ReadAsync($"{Root}/decisions/{hold.Id}", firstUser.Token);
        Assert.Equal("HOLD", held.GetProperty("executionStatus").GetString());
        var pending = await f.ReadAsync($"{Root}/decisions/{unresolved.Id}", firstUser.Token);
        Assert.Equal("UNRESOLVED", pending.GetProperty("executionStatus").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("tradeId").ValueKind);
        Assert.DoesNotContain(decisions, d => d.GetProperty("executionStatus").GetString() == "APPROVED");
        var rejection = Assert.Single((await f.ReadAsync($"{Root}/rejected-decisions", secondUser.Token)).EnumerateArray());
        Assert.Equal(rejected.Id, rejection.GetProperty("decisionId").GetGuid());
        Assert.Equal("LowConfidence", rejection.GetProperty("rejectionReason").GetString());
        Assert.Equal("BUY", rejection.GetProperty("signal").GetString());
        AssertFraction(rejection, "confidence", .1m);
        Assert.Equal("test-original-v1", rejection.GetProperty("modelVersion").GetString());
        AssertUtc(rejection, "rejectedAtUtc");

        var performance = await f.ReadAsync($"{Root}/performance", firstUser.Token);
        using (var scope = f.CreateScope())
        {
            var metrics = await scope.ServiceProvider.GetRequiredService<IAiTraderPerformanceService>().GetMetricsAsync();
            AssertMoney(performance, "totalValue", metrics.TotalValue);
            AssertMoney(performance, "totalPnL", metrics.TotalPnL);
            AssertFraction(performance, "totalReturn", metrics.TotalReturn);
            AssertMoney(performance, "realizedPnL", metrics.RealizedPnL!.Value);
            AssertMoney(performance, "unrealizedPnL", metrics.UnrealizedPnL);
            AssertFraction(performance, "winRate", metrics.WinRate!.Value);
            AssertMoney(performance, "averageWin", metrics.AverageWin!.Value);
            AssertMoney(performance, "averageLoss", metrics.AverageLoss!.Value);
            AssertMoney(performance.GetProperty("bestTrade"), "realizedPnL", metrics.BestTrade!.RealizedPnL);
            AssertMoney(performance.GetProperty("worstTrade"), "realizedPnL", metrics.WorstTrade!.RealizedPnL);
            AssertFraction(performance, "maxDrawdown", metrics.MaxDrawdown!.Value);
        }
        Assert.Equal(4, performance.GetProperty("executedTradeCount").GetInt32());
        Assert.Equal(2, performance.GetProperty("buyTradeCount").GetInt32());
        Assert.Equal(2, performance.GetProperty("sellTradeCount").GetInt32());
        Assert.Equal(1, performance.GetProperty("winningSellCount").GetInt32());
        Assert.Equal(1, performance.GetProperty("losingSellCount").GetInt32());
        Assert.Equal(0, performance.GetProperty("breakEvenSellCount").GetInt32());
        Assert.True(performance.GetProperty("tradeStatisticsComplete").GetBoolean());
        Assert.Equal(0, performance.GetProperty("unvaluedLegacySellCount").GetInt32());
        var history = (await f.ReadAsync($"{Root}/performance/history?limit=200", firstUser.Token)).EnumerateArray().ToArray();
        Assert.Equal(4, history.Length);
        Assert.True(history[0].GetProperty("isInitialBaseline").GetBoolean());
        AssertMoney(history[1], "totalValue", high.TotalValue);
        AssertMoney(history[2], "totalValue", low.TotalValue);
        Assert.Equal(history.Select(p => p.GetProperty("valuedAtUtc").GetDateTime()).Order(),
            history.Select(p => p.GetProperty("valuedAtUtc").GetDateTime()));

        foreach (var user in new[] { firstUser, secondUser })
        {
            var shared = await f.ReadAsync($"{Root}/overview?userId={user.Id}&portfolioId={Guid.NewGuid()}", user.Token);
            Assert.Equal(state.PortfolioId, shared.GetProperty("portfolioId").GetGuid());
            AssertMoney(shared, "totalValue", 100040m);
            foreach (var id in new[] { personalTrade1, personalTrade2 })
            {
                using var unknown = await f.GetAsync($"{Root}/trades/{id}", user.Token);
                Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            }
        }
        Assert.All(trades, t => Assert.Equal("AAPL", t.GetProperty("symbol").GetString()));
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Empty(f.Market.Calls);
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("performance")]
    [InlineData("positions")]
    public async Task Current_reads_value_each_position_once(string route)
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        await f.ExecuteAsync(symbol: "MSFT");
        f.Market.Calls.Clear();
        f.Observer.Reset();
        await f.ReadAsync($"{Root}/{route}", AiTraderApiFixture.Token());
        Assert.Equal(new[] { "AAPL", "MSFT" }, f.Market.Calls.Order());
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    [Theory]
    [InlineData("overview", "missing")]
    [InlineData("positions", "missing")]
    [InlineData("performance", "missing")]
    [InlineData("overview", "currency")]
    [InlineData("positions", "currency")]
    [InlineData("performance", "currency")]
    [InlineData("overview", "zero")]
    [InlineData("positions", "symbol")]
    public async Task Invalid_current_quotes_return_controlled_unavailability_but_persisted_histories_still_work(
        string route, string scenario)
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.InitializeAsync();
        var trade = await f.ExecuteAsync();
        await f.CaptureAsync();
        f.Market.Quote = (symbol, _) => scenario == "missing" ? null : new StockQuote(
            scenario == "symbol" ? "WRONG" : symbol, scenario == "currency" ? "CAD" : "USD",
            scenario == "zero" ? 0m : 120m, null, null, null, f.Time.Now);
        var before = await f.Database.StateAsync();
        f.Observer.Reset();
        using var response = await f.GetAsync($"{Root}/{route}", AiTraderApiFixture.Token());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ai_trader_performance_unavailable", await ErrorAsync(response));
        f.Market.Calls.Clear();
        foreach (var history in new[] { "trades", $"trades/{trade.TradeId}", "decisions",
            $"decisions/{trade.DecisionId}", "rejected-decisions", "performance/history" })
            await f.ReadAsync($"{Root}/{history}", AiTraderApiFixture.Token());
        Assert.Empty(f.Market.Calls);
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    [Theory]
    [InlineData(MarketDataProviderFailure.Timeout, HttpStatusCode.GatewayTimeout, "market_data_provider_timeout")]
    [InlineData(MarketDataProviderFailure.ProviderUnavailable, HttpStatusCode.ServiceUnavailable, "market_data_provider_unavailable")]
    [InlineData(MarketDataProviderFailure.MalformedResponse, HttpStatusCode.BadGateway, "market_data_provider_invalid_response")]
    public async Task Upstream_provider_failures_keep_controlled_HTTP_status_and_do_not_write(
        MarketDataProviderFailure failure, HttpStatusCode status, string error)
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        f.Market.Quote = (_, _) => throw new MarketDataProviderException(failure);
        var before = await f.Database.StateAsync();
        f.Observer.Reset();
        foreach (var route in new[] { "overview", "positions", "performance" })
        {
            using var response = await f.GetAsync($"{Root}/{route}", AiTraderApiFixture.Token());
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(error, await ErrorAsync(response));
        }
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    private static async Task AssertPositionAsync(AiTraderApiFixture f, string token,
        decimal quantity, decimal average, decimal price, decimal pnl)
    {
        var position = Assert.Single((await f.ReadAsync($"{Root}/positions", token)).EnumerateArray());
        Assert.Equal("AAPL", position.GetProperty("symbol").GetString());
        AssertMoney(position, "quantity", quantity);
        AssertMoney(position, "averagePrice", average);
        AssertMoney(position, "currentPrice", price);
        AssertMoney(position, "marketValue", quantity * price);
        AssertMoney(position, "pnL", pnl);
    }

    private static async Task<Guid> PersonalBuyAsync(AiTraderApiFixture f, string token, string symbol)
    {
        f.Market.Prices[symbol] = 100m;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/portfolio/trades");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { orderId = Guid.NewGuid(), side = "BUY", symbol, quantity = 3m, orderType = "market" });
        using var response = await f.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return result.RootElement.GetProperty("transactionId").GetGuid();
    }
}
