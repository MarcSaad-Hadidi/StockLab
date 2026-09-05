using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.MarketData;

/// <summary>
/// Local, deterministic fixtures for development. Prices are simulated, not live market data.
/// Daily bars cover the five sessions from August 24 through August 28, 2026.
/// </summary>
public sealed class MockMarketDataProvider : IMarketDataProvider
{
    private static readonly DateTimeOffset FirstSessionOpen =
        new(2026, 8, 24, 13, 30, 0, TimeSpan.Zero);

    private static readonly SimulatedStock[] Stocks =
    [
        new("AAPL", "Apple Inc.", 200m, 20_000_000),
        new("MSFT", "Microsoft Corporation", 400m, 15_000_000),
        new("NVDA", "NVIDIA Corporation", 120m, 30_000_000)
    ];

    public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedSymbol = NormalizeSymbol(symbol);
        var stock = Array.Find(Stocks, stock => stock.Symbol == normalizedSymbol);
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
            last.OpenTimeUtc.AddHours(6.5)));
    }

    public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var searchTerm = query.Trim();
        var results = new List<StockSearchResult>();

        foreach (var stock in Stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stock.Symbol.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                || stock.CompanyName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new StockSearchResult(stock.Symbol, stock.CompanyName, "NASDAQ", "USD"));
            }
        }

        return Task.FromResult<IReadOnlyList<StockSearchResult>>(results.AsReadOnly());
    }

    public Task<StockHistory?> GetHistoryAsync(
        StockHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var normalizedSymbol = NormalizeSymbol(request.Symbol);

        if (request.FromUtc.Offset != TimeSpan.Zero || request.ToUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("History timestamps must have a zero UTC offset.", nameof(request));
        }

        if (request.FromUtc >= request.ToUtc)
        {
            throw new ArgumentException("FromUtc must precede ToUtc.", nameof(request));
        }

        if (!Enum.IsDefined(request.Interval))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "History interval must be a defined value.");
        }

        if (request.Interval != StockHistoryInterval.Day)
        {
            throw new NotSupportedException("The mock provider supports only daily history.");
        }

        var stock = Array.Find(Stocks, stock => stock.Symbol == normalizedSymbol);
        if (stock is null)
        {
            return Task.FromResult<StockHistory?>(null);
        }

        var bars = CreateDailyBars(stock, cancellationToken)
            .Where(bar => bar.OpenTimeUtc >= request.FromUtc && bar.OpenTimeUtc < request.ToUtc)
            .ToArray();

        return Task.FromResult<StockHistory?>(new StockHistory(
            stock.Symbol, "USD", request.Interval, Array.AsReadOnly(bars)));
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }

    private static StockHistoryBar[] CreateDailyBars(SimulatedStock stock, CancellationToken cancellationToken)
    {
        var bars = new StockHistoryBar[5];
        for (var day = 0; day < bars.Length; day++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var open = stock.InitialPrice + day;
            bars[day] = new StockHistoryBar(
                FirstSessionOpen.AddDays(day), open, open + 1m, open - 0.5m,
                open + 0.5m, stock.InitialVolume + day * 100_000L);
        }

        return bars;
    }

    private sealed record SimulatedStock(string Symbol, string CompanyName, decimal InitialPrice, long InitialVolume);
}
