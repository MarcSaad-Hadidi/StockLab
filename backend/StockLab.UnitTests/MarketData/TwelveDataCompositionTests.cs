using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;
using static StockLab.UnitTests.MarketData.TwelveDataProviderTests;

namespace StockLab.UnitTests.MarketData;

public sealed class TwelveDataCompositionTests
{
    [Theory]
    [InlineData("Website", "WEBSITE-TEST-KEY")]
    [InlineData("Fallback", "FALLBACK-TEST-KEY")]
    public async Task Actual_program_selects_only_active_credential_and_health_is_free(string active, string expected)
    {
        using var handler = new Handler { Respond = (_, _) => Task.FromResult(Response(QuoteJson)) };
        using var app = Application(handler, "TwelveData", active);
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.IsType<TwelveDataProvider>(app.Services.GetRequiredKeyedService<IMarketDataProvider>("Terminal"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Empty(handler.Requests);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/stocks/AAPL/quote")).StatusCode);
        Assert.Equal("apikey " + expected, Assert.Single(handler.Requests).Authorization);
        Assert.DoesNotContain("TEST-KEY", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Actual_program_mock_needs_no_secret_or_external_client()
    {
        using var handler = new Handler { Respond = (_, _) => throw new Exception("Network forbidden") };
        using var app = Application(handler, "Mock", website: null, fallback: null);
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.IsType<MockMarketDataProvider>(app.Services.GetRequiredKeyedService<IMarketDataProvider>("Terminal"));
        foreach (var route in new[] { "/health", "/api/stocks/AAPL/quote", "/api/stocks/search?query=apple",
            "/api/stocks/AAPL/history?from=2026-08-24&to=2026-08-29&interval=Day" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("invalid", "Website", "WEBSITE-TEST-KEY", "FALLBACK-TEST-KEY", 10)]
    [InlineData("TwelveData", "MachineLearning", "WEBSITE-TEST-KEY", "FALLBACK-TEST-KEY", 10)]
    [InlineData("TwelveData", "other", "WEBSITE-TEST-KEY", "FALLBACK-TEST-KEY", 10)]
    [InlineData("TwelveData", "Website", null, "FALLBACK-TEST-KEY", 10)]
    [InlineData("TwelveData", "Fallback", "WEBSITE-TEST-KEY", null, 10)]
    [InlineData("TwelveData", "Website", "WEBSITE-TEST-KEY", "FALLBACK-TEST-KEY", 0)]
    [InlineData("TwelveData", "Website", "WEBSITE-TEST-KEY", "FALLBACK-TEST-KEY", 61)]
    public void Actual_program_rejects_invalid_configuration_at_startup_without_network(
        string provider, string active, string? website, string? fallback, int timeout)
    {
        using var handler = new Handler();
        using var app = Application(handler, provider, active, website, fallback, timeout);
        var error = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("OptionsValidationException", error.ToString());
        Assert.DoesNotContain("TEST-KEY", error.ToString());
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(400, HttpStatusCode.BadGateway, "market_data_provider_invalid_response")]
    [InlineData(401, HttpStatusCode.ServiceUnavailable, "market_data_provider_unavailable")]
    [InlineData(403, HttpStatusCode.ServiceUnavailable, "market_data_provider_unavailable")]
    [InlineData(404, HttpStatusCode.ServiceUnavailable, "market_data_provider_unavailable")]
    [InlineData(429, HttpStatusCode.ServiceUnavailable, "market_data_provider_rate_limited")]
    [InlineData(500, HttpStatusCode.ServiceUnavailable, "market_data_provider_unavailable")]
    public async Task Real_api_pipeline_returns_safe_upstream_errors(int status, HttpStatusCode expected, string code)
    {
        using var handler = new Handler { Respond = (_, _) => Task.FromResult(Response(
            $$"""{"status":"error","code":{{status}},"message":"WEBSITE-TEST-KEY FALLBACK-TEST-KEY ML-TEST-KEY"}""", (HttpStatusCode)status)) };
        using var app = Application(handler, "TwelveData");
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.GetAsync("/api/stocks/AAPL/quote");
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("TEST-KEY", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetString());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Full_pipeline_bursts_cache_expiration_and_local_rejection_preserve_credits()
    {
        using var fixture = new Fixture(QuoteJson);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Respond = async (_, token) => { await gate.Task.WaitAsync(token); return Response(QuoteJson); };
        var clock = new Clock();
        using var memory = new MemoryCache(new MemoryCacheOptions { Clock = clock, SizeLimit = 8 * 1024 * 1024 });
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 2, QueueLimit = 0, Window = TimeSpan.FromHours(1), AutoReplenishment = false
        });
        var limited = new RateLimitedMarketDataProvider(fixture.Provider, limiter, NullLogger<RateLimitedMarketDataProvider>.Instance);
        var dedup = new DeduplicatingMarketDataProvider(limited);
        var cache = new CachingMarketDataProvider(dedup, memory, Options.Create(new MarketDataCacheOptions { QuoteTtl = TimeSpan.FromSeconds(10) }));
        Task<StockQuote?>[] Burst() => Enumerable.Range(0, 10).Select(_ => cache.GetQuoteAsync(" aapl ")).ToArray();
        var first = Burst();
        Assert.All(first, t => Assert.False(t.IsCompleted));
        Assert.Equal(1, dedup.InFlightCount); Assert.Single(fixture.Handler.Requests);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
        gate.SetResult();
        var results = await Task.WhenAll(first);
        Assert.All(results, r => Assert.Same(results[0], r));
        await Task.WhenAll(Burst());
        Assert.Single(fixture.Handler.Requests);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
        clock.UtcNow += TimeSpan.FromSeconds(10);
        gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var expired = Burst();
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.Equal(1, dedup.InFlightCount);
        Assert.Equal(0, limiter.GetStatistics()!.CurrentAvailablePermits);
        gate.SetResult();
        await Task.WhenAll(expired);
        await Assert.ThrowsAsync<MarketDataRateLimitException>(() => cache.GetQuoteAsync("MSFT"));
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Theory]
    [InlineData("from=2026-08-24T13:30:00Z&to=2026-08-24T15:30:00Z&interval=Minute", true)]
    [InlineData("from=2026-08-24&to=2026-08-29&interval=Day", false)]
    public async Task Actual_history_api_preserves_temporal_shape(string query, bool intraday)
    {
        using var handler = new Handler { Respond = (_, _) => Task.FromResult(Response(
            intraday ? HistoryJson("1min", "2026-08-24 13:30:00") : HistoryJson())) };
        using var app = Application(handler, "TwelveData");
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.GetAsync("/api/stocks/AAPL/history?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bar = Assert.Single(json.RootElement.GetProperty("bars").EnumerateArray());
        Assert.Equal(intraday ? JsonValueKind.String : JsonValueKind.Null, bar.GetProperty("openTimeUtc").ValueKind);
        Assert.Equal(intraday ? JsonValueKind.Null : JsonValueKind.String, bar.GetProperty("periodDate").ValueKind);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("from=2026-08-24&to=2026-08-29&interval=Minute")]
    [InlineData("from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00Z&interval=Day")]
    [InlineData("from=2026-08-24&to=2026-08-24&interval=Day")]
    [InlineData("from=2026-08-29&to=2026-08-24&interval=Day")]
    [InlineData("from=2026-08-24&from=2026-08-25&to=2026-08-29&interval=Day")]
    public async Task Actual_history_api_rejects_incompatible_or_repeated_bounds_without_network(string query)
    {
        using var handler = new Handler();
        using var app = Application(handler, "TwelveData");
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.GetAsync("/api/stocks/AAPL/history?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_error", json.RootElement.GetProperty("error").GetString());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Calendar_and_intraday_keys_preserve_kind_bounds_interval_and_validation()
    {
        using var fixture = new Fixture(HistoryJson());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Respond = async (request, token) =>
        {
            await gate.Task.WaitAsync(token);
            var interval = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query)["interval"].ToString();
            return Response(HistoryJson(interval, interval is "1min" or "1h" ? "2026-08-24 13:30:00" : "2026-08-24"));
        };
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var dedup = new DeduplicatingMarketDataProvider(fixture.Provider);
        var cache = new CachingMarketDataProvider(dedup, memory, Options.Create(new MarketDataCacheOptions()));
        var day = Calendar();
        var minute = Intraday();
        var minuteRange = (IntradayHistoryRange)minute.Range;
        StockHistoryRequest[] requests =
        [
            day, day with { Range = new CalendarHistoryRange(new(2026, 8, 25), new(2026, 8, 29)) },
            day with { Range = new CalendarHistoryRange(new(2026, 8, 24), new(2026, 8, 30)) },
            Calendar(StockHistoryInterval.Week), minute,
            minute with { Range = minuteRange with { FromUtc = minuteRange.FromUtc.AddMinutes(1) } },
            minute with { Range = minuteRange with { ToUtc = minuteRange.ToUtc.AddMinutes(1) } },
            Intraday(StockHistoryInterval.Hour)
        ];
        var tasks = requests.Select(r => cache.GetHistoryAsync(r)).ToArray();
        var same = cache.GetHistoryAsync(day with { Symbol = " aapl " });
        Assert.Equal(requests.Length, fixture.Handler.Requests.Count);
        Assert.Equal(requests.Length, dedup.InFlightCount);
        var invalid = minute with { Range = minuteRange with { FromUtc = minuteRange.FromUtc.ToOffset(TimeSpan.FromHours(2)) } };
        await Assert.ThrowsAsync<ArgumentException>(() => dedup.GetHistoryAsync(invalid));
        gate.SetResult();
        await Task.WhenAll(tasks); await same;
        await Task.WhenAll(requests.Select(r => cache.GetHistoryAsync(r)));
        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetHistoryAsync(invalid));
        Assert.Equal(requests.Length, fixture.Handler.Requests.Count);
    }

    private sealed class Clock : ISystemClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch; }

    internal static WebApplicationFactory<Program> Application(Handler handler, string provider, string active = "Website",
        string? website = "WEBSITE-TEST-KEY", string? fallback = "FALLBACK-TEST-KEY", int timeout = 10) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Provider"] = provider, ["TwelveData:ActiveWebsiteKey"] = active,
                ["TwelveData:TimeoutSeconds"] = timeout.ToString(),
                ["TwelveData:Keys:Website"] = website, ["TwelveData:Keys:Fallback"] = fallback,
                ["TWELVE_DATA_ML_API_KEY"] = "ML-TEST-KEY"
            }));
            builder.ConfigureServices(services => services.AddHttpClient(TwelveDataProvider.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler));
        });
}
