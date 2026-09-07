using Microsoft.AspNetCore.Mvc;
using StockLab.Api.Controllers;
using StockLab.Api.DTOs;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.UnitTests.Api;

public sealed class StocksControllerTests
{
    [Fact]
    public async Task Known_quote_returns_200_with_current_fixture()
    {
        var result = await new StocksController(new MockMarketDataProvider()).GetQuoteAsync(" aapl ", default);
        var response = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, response.StatusCode);
        var quote = Assert.IsType<StockQuoteResponse>(response.Value);
        Assert.Equal("AAPL", quote.Symbol);
        Assert.Equal(204.5m, quote.Price);
    }

    [Fact]
    public async Task Unknown_quote_keeps_404_error_contract()
    {
        var result = await new StocksController(new MockMarketDataProvider()).GetQuoteAsync("INVALID", default);
        var response = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(404, response.StatusCode);
        Assert.Equal(new ApiErrorResponse("stock_not_found", "Stock symbol 'INVALID' was not found."), response.Value);
    }

    [Fact]
    public async Task Provider_argument_exception_propagates_to_global_handler()
    {
        var exception = new ArgumentException("provider detail");
        var controller = new StocksController(new ThrowingProvider(exception));
        Assert.Same(exception, await Assert.ThrowsAsync<ArgumentException>(() => controller.GetQuoteAsync("AAPL", default)));
    }

    [Fact]
    public async Task Request_cancellation_token_is_forwarded_to_provider()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var controller = new StocksController(new MockMarketDataProvider());
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.GetQuoteAsync("AAPL", cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Search_forwards_request_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var controller = new StocksController(new MockMarketDataProvider());
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.SearchStocksAsync("AAPL", cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Search_provider_failure_propagates_to_global_handler()
    {
        var exception = new InvalidOperationException("provider detail");
        var controller = new StocksController(new ThrowingProvider(exception));
        Assert.Same(exception, await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SearchStocksAsync("AAPL", default)));
    }

    private sealed class ThrowingProvider(Exception exception) : IMarketDataProvider
    {
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) => Task.FromException<StockQuote?>(exception);
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<StockSearchResult>>(exception);
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
