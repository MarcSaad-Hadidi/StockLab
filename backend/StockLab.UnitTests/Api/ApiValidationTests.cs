using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockLab.Api.Controllers;
using StockLab.Api.Middleware;
using StockLab.Api.Validation;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.UnitTests.Api;

public sealed class ApiValidationTests : IDisposable
{
    private readonly CountingProvider provider = new();
    private readonly IHost host;
    private readonly HttpClient client;

    public ApiValidationTests()
    {
        host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IMarketDataProvider>(provider);
                services.AddControllers()
                    .AddApplicationPart(typeof(StocksController).Assembly)
                    .AddApplicationPart(typeof(ValidationProbeController).Assembly)
                    .ConfigureApiBehaviorOptions(ApiValidation.Configure);
            })
            .Configure(app =>
            {
                app.UseMiddleware<ExceptionHandlingMiddleware>();
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).Start();
        client = host.GetTestClient();
    }

    [Theory]
    [InlineData("/api/stocks/%20/quote", "symbol")]
    [InlineData("/api/stocks/%20%20%20/quote", "symbol")]
    [InlineData("/api/stocks/search", "query")]
    [InlineData("/api/stocks/search?query=", "query")]
    [InlineData("/api/stocks/search?query=%20%20%20", "query")]
    [InlineData("/validation-probe", "value")]
    [InlineData("/validation-probe?value=", "value")]
    [InlineData("/validation-probe?value=%20%20", "value")]
    [InlineData("/validation-probe?value=invalid-format", "value")]
    [InlineData("/validation-probe?value=ABC&count=0", "count")]
    [InlineData("/validation-probe?value=ABC&count=11", "count")]
    [InlineData("/validation-probe?value=ABC&count=private-marker", "count")]
    [InlineData("/validation-probe?value=ABC&mode=999", "mode")]
    [InlineData("/validation-probe?value=ABC&mode=private-marker", "mode")]
    public async Task Invalid_input_returns_safe_field_errors_before_provider_call(string url, string field)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
        Assert.Equal("validation_error", json.RootElement.GetProperty("error").GetString());
        Assert.Equal("The request contains invalid data.", json.RootElement.GetProperty("message").GetString());
        var errors = json.RootElement.GetProperty("errors");
        Assert.Equal("The value is missing or invalid.", errors.GetProperty(field)[0].GetString());
        Assert.DoesNotContain("private-marker", body);
        Assert.Equal(0, provider.QuoteCalls);
        Assert.Equal(0, provider.SearchCalls);
    }

    [Theory]
    [InlineData("AAPL", "AAPL", "Apple Inc.")]
    [InlineData("apple", "AAPL", "Apple Inc.")]
    [InlineData("microsoft", "MSFT", "Microsoft Corporation")]
    [InlineData("nvidia", "NVDA", "NVIDIA Corporation")]
    [InlineData("aPpLe", "AAPL", "Apple Inc.")]
    [InlineData("%20aapl%20", "AAPL", "Apple Inc.")]
    public async Task Search_returns_stock_list_with_http_fields(string query, string symbol, string companyName)
    {
        using var response = await client.GetAsync($"/api/stocks/search?query={query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var stock = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(4, stock.EnumerateObject().Count());
        Assert.Equal(symbol, stock.GetProperty("symbol").GetString());
        Assert.Equal(companyName, stock.GetProperty("companyName").GetString());
        Assert.Equal("NASDAQ", stock.GetProperty("exchange").GetString());
        Assert.Equal("USD", stock.GetProperty("currency").GetString());
        Assert.Equal(1, provider.SearchCalls);
    }

    [Fact]
    public async Task Search_without_matches_returns_200_and_empty_array()
    {
        using var response = await client.GetAsync("/api/stocks/search?query=zzzzzz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, provider.SearchCalls);
    }

    [Theory]
    [InlineData("AAPL", HttpStatusCode.OK)]
    [InlineData("%20aapl%20", HttpStatusCode.OK)]
    [InlineData("INVALID", HttpStatusCode.NotFound)]
    public async Task Valid_symbols_reach_provider_and_keep_quote_behavior(string symbol, HttpStatusCode expected)
    {
        using var response = await client.GetAsync($"/api/stocks/{symbol}/quote");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(1, provider.QuoteCalls);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal("AAPL", json.RootElement.GetProperty("symbol").GetString());
            Assert.Equal(204.5m, json.RootElement.GetProperty("price").GetDecimal());
        }
        else
        {
            Assert.Equal("stock_not_found", json.RootElement.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task Missing_route_segment_remains_404_without_calling_provider()
    {
        using var response = await client.GetAsync("/api/stocks/quote");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, provider.QuoteCalls);
    }

    [Fact]
    public async Task Valid_probe_reaches_action_so_rejection_tests_are_not_false_positives()
    {
        using var response = await client.GetAsync("/validation-probe?value=ABC&count=2&mode=Second");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, provider.QuoteCalls);
    }

    public void Dispose()
    {
        client.Dispose();
        host.Dispose();
    }

    private const string FullHistoryQuery = "from=2026-08-24&to=2026-08-29&interval=Day";

    [Theory]
    [InlineData("from=2026-08-24&to=2026-08-29&interval=Day", 5)]
    [InlineData("from=2026-08-25&to=2026-08-27&interval=Day", 2)]
    [InlineData("from=2026-09-01&to=2026-09-02&interval=Day", 0)]
    public async Task History_returns_ohlcv_in_requested_half_open_range(string query, int count)
    {
        using var response = await client.GetAsync($"/api/stocks/%20aapl%20/history?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("AAPL", root.GetProperty("symbol").GetString());
        Assert.Equal("USD", root.GetProperty("currency").GetString());
        Assert.Equal("Day", root.GetProperty("interval").GetString());
        var bars = root.GetProperty("bars").EnumerateArray().ToArray();
        Assert.Equal(count, bars.Length);
        var from = DateOnly.Parse(query.Split('&')[0][5..]);
        var to = DateOnly.Parse(query.Split('&')[1][3..]);
        DateOnly? previous = null;
        foreach (var bar in bars)
        {
            Assert.Equal(7, bar.EnumerateObject().Count());
            var timestamp = DateOnly.Parse(bar.GetProperty("periodDate").GetString()!);
            Assert.Equal(JsonValueKind.Null, bar.GetProperty("openTimeUtc").ValueKind);
            Assert.True(timestamp >= from && timestamp < to);
            Assert.True(previous is null || timestamp > previous);
            previous = timestamp;
            var open = bar.GetProperty("open").GetDecimal();
            var high = bar.GetProperty("high").GetDecimal();
            var low = bar.GetProperty("low").GetDecimal();
            var close = bar.GetProperty("close").GetDecimal();
            Assert.True(low <= open && low <= close && high >= open && high >= close);
            Assert.True(bar.GetProperty("volume").GetInt64() >= 0);
        }
        Assert.Equal(1, provider.HistoryCalls);
    }

    [Theory]
    [InlineData("%20", FullHistoryQuery)]
    [InlineData("AAPL", "to=2026-08-29T13:30:00Z&interval=Day")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&interval=Day")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00Z")]
    [InlineData("AAPL", "from=invalid&to=2026-08-29T13:30:00Z&interval=Day")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=invalid&interval=Day")]
    [InlineData("AAPL", "from=2026-08-29T13:30:00Z&to=2026-08-29T13:30:00Z&interval=Day")]
    [InlineData("AAPL", "from=2026-08-30T13:30:00Z&to=2026-08-29T13:30:00Z&interval=Day")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00%2B02:00&to=2026-08-29T13:30:00Z&interval=Day")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00%2B02:00&interval=Day")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00Z&interval=999")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00Z&interval=invalid")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00Z&interval=Minute,Hour")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00Z&interval=1,2")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00Z&interval=Day&interval=Hour")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00&to=2026-08-29T13:30:00Z&interval=Day")]
    [InlineData("AAPL", "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00&interval=Day")]
    public async Task Invalid_history_is_rejected_before_provider(string symbol, string query)
    {
        using var response = await client.GetAsync($"/api/stocks/{symbol}/history?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_error", json.RootElement.GetProperty("error").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(0, provider.HistoryCalls);
    }

    [Fact]
    public async Task Unknown_history_symbol_returns_stock_not_found()
    {
        using var response = await client.GetAsync($"/api/stocks/INVALID/history?{FullHistoryQuery}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("stock_not_found", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(1, provider.HistoryCalls);
    }

    [Theory]
    [InlineData("Minute")]
    [InlineData("Hour")]
    [InlineData("Week")]
    [InlineData("Month")]
    public async Task Unsupported_history_interval_returns_explicit_safe_error(string interval)
    {
        using var response = await client.GetAsync($"/api/stocks/AAPL/history?{(interval is "Minute" or "Hour" ? "from=2026-08-24T13:30:00Z&to=2026-08-29T13:30:00Z&interval=" + interval : FullHistoryQuery.Replace("Day", interval))}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unsupported_operation", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(1, provider.HistoryCalls);
    }
    [Theory]
    [InlineData("from=2026-08-24T13:30:00%2B00:00&to=2026-08-29T13:30:00%2B00:00&interval=Minute")]
    [InlineData("from=2026-08-24T13:30:00.000Z&to=2026-08-29T13:30:00.000Z&interval=1")]
    public async Task Explicit_utc_bounds_and_single_intervals_remain_supported(string query)
    {
        using var response = await client.GetAsync($"/api/stocks/AAPL/history?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unsupported_operation", json.RootElement.GetProperty("error").GetString());

        Assert.Equal(1, provider.HistoryCalls);
    }
    private sealed class CountingProvider : IMarketDataProvider
    {
        private readonly MockMarketDataProvider inner = new();
        public int QuoteCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public int HistoryCalls { get; private set; }
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            QuoteCalls++;
            return inner.GetQuoteAsync(symbol, cancellationToken);
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return inner.SearchStocksAsync(query, cancellationToken);
        }
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
        {
            HistoryCalls++;
            return inner.GetHistoryAsync(request, cancellationToken);
        }
    }
}

// Test-only controller: exercises native query binding and annotations without
// introducing future endpoint DTOs or artificial routes into the production API.
[ApiController]
[Route("validation-probe")]
public sealed class ValidationProbeController(IMarketDataProvider provider) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery, Required, RegularExpression("^[A-Z]+$")] string value,
        [FromQuery, Range(1, 10)] int count = 1,
        [FromQuery, EnumDataType(typeof(ProbeMode))] ProbeMode mode = ProbeMode.First)
    {
        await provider.GetQuoteAsync("AAPL", HttpContext.RequestAborted);
        return Ok();
    }
}

public enum ProbeMode { First, Second }
