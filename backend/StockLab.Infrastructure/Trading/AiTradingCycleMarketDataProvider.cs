using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

/// <summary>Cycle-only quote floor: the source observation must cover the required daily session.</summary>
public sealed class AiTradingCycleMarketDataProvider(IMarketDataProvider inner, TimeProvider clock,
    DateOnly? minimumSession) : IMarketDataProvider
{
    public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (minimumSession is null) throw new MarketDataProviderException(MarketDataProviderFailure.DataUnavailable);
        var quote = await inner.GetQuoteAsync(symbol, cancellationToken);
        if (quote is not null && (quote.AsOfUtc.Offset != TimeSpan.Zero || quote.AsOfUtc > clock.GetUtcNow()
            || DateOnly.FromDateTime(quote.AsOfUtc.UtcDateTime) < minimumSession))
            throw new MarketDataProviderException(MarketDataProviderFailure.DataUnavailable);
        return quote;
    }

    public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
        => inner.SearchStocksAsync(query, cancellationToken);

    public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
        => inner.GetHistoryAsync(request, cancellationToken);
}
