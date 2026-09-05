using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.UnitTests.MarketData;

public sealed class MockMarketDataProviderTests
{
    private readonly IMarketDataProvider provider = new MockMarketDataProvider();
    private static readonly DateTimeOffset FirstOpen = new(2026, 8, 24, 13, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(" aapl ", "AAPL", 204.5, 20_400_000L)]
    [InlineData("msft", "MSFT", 404.5, 15_400_000L)]
    [InlineData("NvDa", "NVDA", 124.5, 30_400_000L)]
    public async Task Quote_returns_normalized_simulated_prices(string input, string symbol, double price, long volume)
    {
        var quote = await provider.GetQuoteAsync(input);

        Assert.NotNull(quote);
        Assert.Equal(symbol, quote.Symbol);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal((decimal)price, quote.Price);
        Assert.Equal(volume, quote.Volume);
    }

    [Fact]
    public async Task Unknown_symbol_returns_no_quote_or_history()
    {
        Assert.Null(await provider.GetQuoteAsync("UNKNOWN"));
        Assert.Null(await provider.GetHistoryAsync(Request("UNKNOWN")));
    }

    [Theory]
    [InlineData(" aap ", "AAPL")]
    [InlineData("microsoft", "MSFT")]
    [InlineData("NVIDIA", "NVDA")]
    public async Task Search_matches_symbol_or_company_name(string query, string expectedSymbol)
    {
        var result = Assert.Single(await provider.SearchStocksAsync(query));
        Assert.Equal(expectedSymbol, result.Symbol);
    }

    [Fact]
    public async Task Unmatched_search_returns_empty_collection()
    {
        Assert.Empty(await provider.SearchStocksAsync("no such company"));
    }

    [Fact]
    public async Task History_uses_inclusive_start_and_exclusive_end()
    {
        var request = Request(" aapl ") with { FromUtc = FirstOpen.AddDays(1), ToUtc = FirstOpen.AddDays(3) };
        var history = await provider.GetHistoryAsync(request);

        Assert.NotNull(history);
        Assert.Equal("AAPL", history.Symbol);
        Assert.Equal(StockHistoryInterval.Day, history.Interval);
        Assert.Equal(new[] { FirstOpen.AddDays(1), FirstOpen.AddDays(2) }, history.Bars.Select(bar => bar.OpenTimeUtc));
        Assert.All(history.Bars, bar =>
        {
            Assert.True(bar.Low <= bar.Open && bar.Low <= bar.Close);
            Assert.True(bar.High >= bar.Open && bar.High >= bar.Close);
            Assert.True(bar.Volume >= 0);
        });
    }

    [Fact]
    public async Task History_is_repeatable_and_agrees_with_quote()
    {
        var history = await provider.GetHistoryAsync(Request("AAPL"));
        var repeated = await provider.GetHistoryAsync(Request("AAPL"));
        var quote = await provider.GetQuoteAsync("AAPL");

        Assert.NotNull(history);
        Assert.NotNull(repeated);
        Assert.NotNull(quote);
        Assert.Equal(5, history.Bars.Count);
        Assert.Equal(history.Bars.ToArray(), repeated.Bars.ToArray());
        Assert.Equal(history.Bars[^1].Close, quote.Price);
        Assert.Equal(history.Bars[^1].Volume, quote.Volume);
        Assert.Equal(history.Bars[^1].Close - history.Bars[^2].Close, quote.Change);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_inputs_are_rejected(string? input)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.GetQuoteAsync(input!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => provider.SearchStocksAsync(input!));
    }

    [Fact]
    public async Task Reversed_history_range_is_rejected()
    {
        var request = Request("AAPL") with { ToUtc = FirstOpen.AddDays(-1) };
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetHistoryAsync(request));
    }

    [Fact]
    public async Task Cancellation_preserves_callers_token_for_all_operations()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        var quoteError = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetQuoteAsync("AAPL", source.Token));
        var searchError = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SearchStocksAsync("Apple", source.Token));
        var historyError = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetHistoryAsync(Request("AAPL"), source.Token));

        Assert.Equal(source.Token, quoteError.CancellationToken);
        Assert.Equal(source.Token, searchError.CancellationToken);
        Assert.Equal(source.Token, historyError.CancellationToken);
    }

    private static StockHistoryRequest Request(string symbol) =>
        new(symbol, FirstOpen, FirstOpen.AddDays(5), StockHistoryInterval.Day);
}
