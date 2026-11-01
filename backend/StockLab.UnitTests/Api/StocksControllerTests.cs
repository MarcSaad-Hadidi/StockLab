using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Controllers;
using StockLab.Api.DTOs;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;
using System.Text.Json;
using StockLab.Api.Configuration;
using Microsoft.Extensions.Options;

namespace StockLab.UnitTests.Api;

public sealed class StocksControllerTests
{
    private static readonly IOptions<MarketDataProviderOptions> MockOptions = Options.Create(new MarketDataProviderOptions { Provider = "Mock" });

    [Fact]
    public async Task Provider_metadata_uses_the_terminal_configuration_and_does_not_claim_live_prices()
    {
        // Isolated transport double: no external request or credentials are used.
        var controller = new StocksController(new MockMarketDataProvider(), Options.Create(new MarketDataProviderOptions { Provider = "TwelveData" }));
        var result = await controller.GetQuoteAsync("AAPL", default);
        var quote = Assert.IsType<StockQuoteResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("TwelveData", quote.Source);
        Assert.Equal(2026, quote.AsOfUtc.Year);
        var history = await controller.GetHistoryAsync("AAPL", new StockHistoryQuery { From = "2026-08-24", To = "2026-08-29", Interval = StockHistoryInterval.Day }, default);
        Assert.Equal("TwelveData", Assert.IsType<StockHistoryResponse>(Assert.IsType<OkObjectResult>(history.Result).Value).Source);
    }

    [Fact]
    public async Task Quote_exposes_its_simulated_source_without_changing_the_observation_time()
    {
        var result = await new StocksController(new MockMarketDataProvider(), MockOptions).GetQuoteAsync("AAPL", default);
        var quote = Assert.IsType<StockQuoteResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var json = JsonSerializer.SerializeToElement(quote, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(json.TryGetProperty("source", out var source));
        Assert.Equal("Mock", source.GetString());
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 20, 0, 0, TimeSpan.Zero), quote.AsOfUtc);
    }

    [Fact]
    public async Task Known_quote_returns_200_with_current_fixture()
    {
        var result = await new StocksController(new MockMarketDataProvider(), MockOptions).GetQuoteAsync(" aapl ", default);
        var response = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, response.StatusCode);
        var quote = Assert.IsType<StockQuoteResponse>(response.Value);
        Assert.Equal("AAPL", quote.Symbol);
        Assert.Equal(204.5m, quote.Price);
    }

    [Fact]
    public async Task Unknown_quote_keeps_404_error_contract()
    {
        var result = await new StocksController(new MockMarketDataProvider(), MockOptions).GetQuoteAsync("INVALID", default);
        var response = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(404, response.StatusCode);
        Assert.Equal(new ApiErrorResponse("stock_not_found", "Stock symbol 'INVALID' was not found."), response.Value);
    }

    [Fact]
    public async Task Provider_argument_exception_propagates_to_global_handler()
    {
        var exception = new ArgumentException("provider detail");
        var controller = new StocksController(new ThrowingProvider(exception), MockOptions);
        Assert.Same(exception, await Assert.ThrowsAsync<ArgumentException>(() => controller.GetQuoteAsync("AAPL", default)));
    }

    [Fact]
    public async Task Request_cancellation_token_is_forwarded_to_provider()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var controller = new StocksController(new MockMarketDataProvider(), MockOptions);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.GetQuoteAsync("AAPL", cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Search_forwards_request_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var controller = new StocksController(new MockMarketDataProvider(), MockOptions);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.SearchStocksAsync("AAPL", cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Search_provider_failure_propagates_to_global_handler()
    {
        var exception = new InvalidOperationException("provider detail");
        var controller = new StocksController(new ThrowingProvider(exception), MockOptions);
        Assert.Same(exception, await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SearchStocksAsync("AAPL", default)));
    }

    [Fact]
    public async Task History_forwards_request_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var query = new StockHistoryQuery
        {
            From = "2026-08-24",
            To = "2026-08-29",
            Interval = StockHistoryInterval.Day
        };
        var controller = new StocksController(new MockMarketDataProvider(), MockOptions);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.GetHistoryAsync("AAPL", query, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private sealed class ThrowingProvider(Exception exception) : IMarketDataProvider
    {
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) => Task.FromException<StockQuote?>(exception);
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<StockSearchResult>>(exception);
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
