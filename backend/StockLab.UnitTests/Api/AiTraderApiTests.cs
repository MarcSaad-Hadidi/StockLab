using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.UnitTests.Api;

public sealed class AiTraderApiTests
{
    private const string Root = "/api/ai-trader";

    public static TheoryData<string> Routes => new()
    {
        "overview", "positions", "trades", "trades/11111111-1111-1111-1111-111111111111",
        "decisions", "decisions/11111111-1111-1111-1111-111111111111", "rejected-decisions",
        "performance", "performance/history", "models", "backtests"
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Every_route_requires_a_valid_unexpired_JWT(string route)
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        foreach (var token in new string?[] { null, "not-a-jwt", AiTraderApiFixture.Token(expired: true),
            AiTraderApiFixture.Token(invalidSignature: true) })
        {
            using var response = await f.GetAsync($"{Root}/{route}", token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("unauthorized", await ErrorAsync(response));
        }
        Assert.Empty(f.Market.Calls);
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    [Fact]
    public async Task Missing_AI_portfolio_returns_not_initialized_without_creating_any_state()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        var account = await f.RegisterAsync();
        var before = await f.Database.StateAsync();
        f.Observer.Reset();
        foreach (var route in new[] { "overview", "positions", "performance", "performance/history" })
        {
            using var response = await f.GetAsync($"{Root}/{route}", account.Token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("ai_trader_not_initialized", await ErrorAsync(response));
        }
        foreach (var route in new[] { "trades", "decisions", "rejected-decisions" })
            Assert.Empty((await f.ReadAsync($"{Root}/{route}", account.Token)).EnumerateArray());
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Empty(f.Market.Calls);
    }

    [Fact]
    public async Task Initialized_empty_portfolio_returns_real_baseline_and_nullable_sale_metrics()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        var account = await f.RegisterAsync();
        var state = await f.InitializeAsync();
        var before = await f.Database.StateAsync();
        f.Observer.Reset();
        foreach (var route in new[] { "overview", "performance" })
        {
            var result = await f.ReadAsync($"{Root}/{route}", account.Token);
            Assert.Equal(state.PortfolioId, result.GetProperty("portfolioId").GetGuid());
            Assert.Equal("USD", result.GetProperty("currency").GetString());
            AssertMoney(result, "initialCapital", 100000m);
            AssertMoney(result, "cashBalance", 100000m);
            AssertMoney(result, "positionsMarketValue", 0m);
            AssertMoney(result, "totalValue", 100000m);
            AssertMoney(result, "totalPnL", 0m);
            AssertFraction(result, "totalReturn", 0m);
            AssertFraction(result, "winRate", 0m);
            Assert.Null(result.GetProperty("maxDrawdown").GetString());
            Assert.Equal(0, result.GetProperty("executedTradeCount").GetInt32());
            Assert.Equal(0, result.GetProperty("openPositionsCount").GetInt32());
            Assert.Equal(1, result.GetProperty("historicalCoverage").GetProperty("valuationSampleCount").GetInt32());
            AssertUtc(result, "valuationTimestampUtc");
            if (route == "performance")
            {
                foreach (var field in new[] { "averageWin", "averageLoss", "bestTrade", "worstTrade" })
                    Assert.Equal(JsonValueKind.Null, result.GetProperty(field).ValueKind);
                AssertMoney(result, "realizedPnL", 0m);
                AssertMoney(result, "unrealizedPnL", 0m);
                Assert.True(result.GetProperty("tradeStatisticsComplete").GetBoolean());
            }
        }
        foreach (var route in new[] { "positions", "trades", "decisions", "rejected-decisions" })
            Assert.Empty((await f.ReadAsync($"{Root}/{route}", account.Token)).EnumerateArray());
        var baseline = Assert.Single((await f.ReadAsync($"{Root}/performance/history", account.Token)).EnumerateArray());
        Assert.True(baseline.GetProperty("isInitialBaseline").GetBoolean());
        AssertMoney(baseline, "totalValue", 100000m);
        AssertFraction(baseline, "return", 0m);
        Assert.Equal(before, await f.Database.StateAsync());
        Assert.Equal(0, f.Observer.SaveCalls);
        Assert.Empty(f.Market.Calls);
    }

    public static TheoryData<string> InvalidRequests => new()
    {
        "trades?limit=0", "trades?limit=201", "trades?limit=-1", "trades?limit=abc",
        "decisions?limit=0", "decisions?limit=201", "decisions?limit=abc",
        "rejected-decisions?limit=0", "rejected-decisions?limit=201", "rejected-decisions?limit=abc",
        "performance/history?limit=0", "performance/history?limit=201", "performance/history?limit=abc",
        "trades/not-a-guid", "trades/00000000-0000-0000-0000-000000000000",
        "decisions/not-a-guid", "decisions/00000000-0000-0000-0000-000000000000",
        "performance/history?startDateUtc=not-a-date", "performance/history?endDateUtc=2026-10-03",
        "performance/history?startDateUtc=2026-10-03T12:00:00",
        "performance/history?startDateUtc=2026-10-03T12:00:00Z%0A",
        "performance/history?startDateUtc=2026-10-03T12:00:00%2B01:00",
        "performance/history?startDateUtc=2026-10-04T12:00:00Z&endDateUtc=2026-10-03T12:00:00Z"
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_limits_ids_and_non_UTC_ranges_return_400_without_reading_market(string path)
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        using var response = await f.GetAsync($"{Root}/{path}", AiTraderApiFixture.Token());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await ErrorAsync(response)));
        Assert.Empty(f.Market.Calls);
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    [Theory]
    [InlineData("trades")]
    [InlineData("decisions")]
    public async Task Unknown_valid_detail_id_returns_404(string route)
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        using var response = await f.GetAsync($"{Root}/{route}/{Guid.NewGuid()}", AiTraderApiFixture.Token());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await ErrorAsync(response)));
    }

    internal static void AssertMoney(JsonElement value, string property, decimal expected)
    {
        var money = value.GetProperty(property);
        Assert.Equal(JsonValueKind.String, money.ValueKind);
        Assert.Equal(expected, decimal.Parse(money.GetString()!, CultureInfo.InvariantCulture));
        Assert.DoesNotContain(",", money.GetString()!);
    }

    internal static void AssertFraction(JsonElement value, string property, decimal expected)
    {
        var fraction = value.GetProperty(property);
        Assert.Equal(JsonValueKind.Number, fraction.ValueKind);
        Assert.Equal(expected, fraction.GetDecimal());
    }

    internal static void AssertUtc(JsonElement value, string property)
    {
        var timestamp = value.GetProperty(property).GetString()!;
        Assert.True(timestamp.EndsWith('Z') || timestamp.EndsWith("+00:00", StringComparison.Ordinal), timestamp);
        Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture).Offset);
    }

    internal static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }
}
