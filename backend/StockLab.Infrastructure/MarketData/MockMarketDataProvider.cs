using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.MarketData;

/// <summary>
/// Local, deterministic fixtures for development. Prices are simulated, not live market data.
/// Daily bars cover the five sessions from August 24 through August 28, 2026.
/// </summary>
public sealed class MockMarketDataProvider : IMarketDataProvider
{
    private static readonly DateOnly FirstSessionDate =
        new(2026, 8, 24);

    private static readonly SimulatedStock[] Assets =
    [
        new("AAPL", "Apple Inc.", 200m, 20_000_000),
        new("MSFT", "Microsoft Corporation", 400m, 15_000_000),
        new("NVDA", "NVIDIA Corporation", 120m, 30_000_000),
        new("SPY", "SPDR S&P 500 ETF Trust", 550m, 12_000_000, "NYSE", "ETF", MarketAssetCategory.Etfs),
        new("SPX", "S&P 500 Index", 5_500m, 0, "INDEX", "Index", MarketAssetCategory.Indices),
        new("BTC/USD", "Bitcoin US Dollar", 60_000m, 100_000, "BINANCE", "Digital Currency", MarketAssetCategory.Crypto)
    ];

    public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stock = FindAsset(symbol);
        if (stock is null)
        {
            return Task.FromResult<StockQuote?>(null);
        }

        var bars = CreateDailyBars(stock, cancellationToken);
        var last = bars[^1];
        var previousClose = bars[^2].Close;
        var change = last.Close - previousClose;

        return Task.FromResult<StockQuote?>(new StockQuote(
            stock.Symbol,
            "USD",
            last.Close,
            change,
            decimal.Round(change / previousClose * 100m, 4),
            last.Volume,
            new DateTimeOffset(2026, 8, 28, 20, 0, 0, TimeSpan.Zero),
            stock.CompanyName, stock.Exchange, last.Open, last.High, last.Low, previousClose,
            (long)bars.Average(bar => bar.Volume!.Value), false));
    }

    public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var searchTerm = query.Trim();
        var results = new List<StockSearchResult>();

        foreach (var stock in Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stock.Symbol.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                || stock.CompanyName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(stock.ToSearchResult());
            }
        }

        return Task.FromResult<IReadOnlyList<StockSearchResult>>(results.AsReadOnly());
    }

    public Task<IReadOnlyList<StockSearchResult>> BrowseAssetsAsync(MarketAssetCategory category,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<StockSearchResult>>(Assets.Where(asset => asset.Category == category)
            .Select(asset => asset.ToSearchResult()).ToArray());
    }

    public Task<StockHistory?> GetHistoryAsync(
        StockHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var stock = FindAsset(request.Symbol);

        request.Validate();
        if (request.Interval != StockHistoryInterval.Day)
        {
            throw new NotSupportedException("The mock provider supports only daily history.");
        }

        if (stock is null)
        {
            return Task.FromResult<StockHistory?>(null);
        }

        var range = (CalendarHistoryRange)request.Range;
        var bars = CreateDailyBars(stock, cancellationToken)
            .Where(bar => bar.PeriodDate >= range.FromDate && bar.PeriodDate < range.ToDate)
            .ToArray();

        return Task.FromResult<StockHistory?>(new StockHistory(
            stock.Symbol, "USD", request.Interval, Array.AsReadOnly(bars)));
    }

    private static SimulatedStock? FindAsset(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var normalized = symbol.Trim().ToUpperInvariant();
        return Array.Find(Assets, asset => normalized == asset.Symbol || normalized == $"{asset.Symbol}:{asset.Exchange}");
    }

    private static StockHistoryBar[] CreateDailyBars(SimulatedStock stock, CancellationToken cancellationToken)
    {
        var bars = new StockHistoryBar[5];
        for (var day = 0; day < bars.Length; day++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var open = stock.InitialPrice + day;
            bars[day] = new StockHistoryBar(
                FirstSessionDate.AddDays(day), open, open + 1m, open - 0.5m,
                open + 0.5m, stock.InitialVolume + day * 100_000L);
        }

        return bars;
    }

    private sealed record SimulatedStock(string Symbol, string CompanyName, decimal InitialPrice, long InitialVolume,
        string Exchange = "NASDAQ", string InstrumentType = "Common Stock", MarketAssetCategory Category = MarketAssetCategory.Stocks)
    {
        public StockSearchResult ToSearchResult() => new(Symbol, CompanyName, Exchange, "USD", InstrumentType);
    }
}
