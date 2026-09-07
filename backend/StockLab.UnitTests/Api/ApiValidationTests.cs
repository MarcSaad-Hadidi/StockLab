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

    private sealed class CountingProvider : IMarketDataProvider
    {
        private readonly MockMarketDataProvider inner = new();
        public int QuoteCalls { get; private set; }
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            QuoteCalls++;
            return inner.GetQuoteAsync(symbol, cancellationToken);
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
