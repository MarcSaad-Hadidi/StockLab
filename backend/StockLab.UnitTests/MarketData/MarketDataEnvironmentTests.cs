using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;
using static StockLab.UnitTests.MarketData.TwelveDataProviderTests;

namespace StockLab.UnitTests.MarketData;

public sealed class MarketDataEnvironmentTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task User_facing_environments_default_to_real_quotes(string environment)
    {
        using var handler = new Handler { Respond = (_, _) => Task.FromResult(Response(QuoteJson)) };
        using var app = TwelveDataCompositionTests.Application(handler, null, environment: environment);
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.IsType<TwelveDataProvider>(app.Services.GetRequiredKeyedService<IMarketDataProvider>("Terminal"));
        Assert.Empty(handler.Requests);
        await client.GetAsync("/health");
        Assert.Empty(handler.Requests);
        using var response = await client.GetAsync("/api/stocks/AAPL/quote");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("TwelveData", body.RootElement.GetProperty("source").GetString());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Staging", null)]
    [InlineData("Production", "Mock")]
    [InlineData("Staging", "Mock")]
    public void Startup_cannot_silently_use_simulation_or_missing_live_credentials(string environment, string? provider)
    {
        using var handler = new Handler { Respond = (_, _) => throw new Exception("Network forbidden") };
        using var app = TwelveDataCompositionTests.Application(handler, provider, website: null, fallback: null, environment: environment);
        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("OptionsValidationException", error.ToString());
        Assert.DoesNotContain("TEST-KEY", error.ToString());
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    public async Task Explicit_local_test_or_simulation_configuration_needs_no_external_credential(string environment, bool allowed)
    {
        using var handler = new Handler { Respond = (_, _) => throw new Exception("Network forbidden") };
        using var app = TwelveDataCompositionTests.Application(handler, "Mock", website: null, fallback: null, environment: environment, allowSimulatedData: allowed);
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/stocks/AAPL/quote")).StatusCode);
        Assert.IsType<MockMarketDataProvider>(app.Services.GetRequiredKeyedService<IMarketDataProvider>("Terminal"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Default_development_configuration_remains_explicitly_simulated()
    {
        using var handler = new Handler { Respond = (_, _) => throw new Exception("Network forbidden") };
        using var app = TwelveDataCompositionTests.Application(handler, null, website: null, fallback: null, environment: "Development");
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.IsType<MockMarketDataProvider>(app.Services.GetRequiredKeyedService<IMarketDataProvider>("Terminal"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/stocks/AAPL/quote")).StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("1D", "Minute", "1min", "2026-09-07T12:30:00Z", "2026-09-08T12:30:00Z", "2026-09-07 13:30:00")]
    [InlineData("5D", "Hour", "1h", "2026-09-03T12:30:00Z", "2026-09-08T12:30:00Z", "2026-09-07 13:30:00")]
    [InlineData("1M", "Day", "1day", "2026-08-08", "2026-09-09", "2026-08-24")]
    [InlineData("3M", "Day", "1day", "2026-06-08", "2026-09-09", "2026-08-24")]
    [InlineData("6M", "Day", "1day", "2026-03-08", "2026-09-09", "2026-08-24")]
    [InlineData("YTD", "Day", "1day", "2026-01-01", "2026-09-09", "2026-08-24")]
    [InlineData("1Y", "Day", "1day", "2025-09-08", "2026-09-09", "2026-08-24")]
    [InlineData("5Y", "Week", "1week", "2021-09-08", "2026-09-09", "2026-08-24")]
    [InlineData("MAX", "Day", "1day", "2013-01-01", "2026-09-09", "2026-08-24")]
    public async Task Every_chart_period_uses_the_real_provider_with_its_existing_temporal_contract(
        string range, string interval, string upstreamInterval, string from, string to, string observation)
    {
        using var handler = new Handler { Respond = (_, _) => Task.FromResult(Response(HistoryJson(upstreamInterval, observation))) };
        using var app = TwelveDataCompositionTests.Application(handler, "TwelveData");
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        var route = $"/api/stocks/AAPL/history?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&interval={interval}";
        using var response = await client.GetAsync(route);
        Assert.True(response.StatusCode == HttpStatusCode.OK, range);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("TwelveData", body.RootElement.GetProperty("source").GetString());
        Assert.Single(body.RootElement.GetProperty("bars").EnumerateArray());
        var request = Assert.Single(handler.Requests);
        Assert.Contains("interval=" + upstreamInterval, Uri.UnescapeDataString(request.Uri.Query));
        Assert.DoesNotContain("outputsize", request.Uri.Query);
        Assert.DoesNotContain("TEST-KEY", request.Uri.ToString());
        if (interval is "Minute" or "Hour") Assert.Contains("timezone=UTC", request.Uri.Query);
        using var cached = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_failed_real_history_request_never_returns_mock_bars()
    {
        using var handler = new Handler { Respond = (_, _) => Task.FromResult(Response(
            """{"status":"error","code":429,"message":"private upstream detail"}""", HttpStatusCode.TooManyRequests)) };
        using var app = TwelveDataCompositionTests.Application(handler, "TwelveData");
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.GetAsync("/api/stocks/AAPL/history?from=2026-08-24&to=2026-08-29&interval=Day");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("market_data_provider_rate_limited", body);
        Assert.DoesNotContain("private upstream detail", body);
        Assert.DoesNotContain("bars", body);
        Assert.Single(handler.Requests);
    }
}
