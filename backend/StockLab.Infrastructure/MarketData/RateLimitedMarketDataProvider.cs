using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.MarketData;

/// <summary>One shared outbound budget across all market-data operations.</summary>
public sealed class RateLimitedMarketDataProvider(
    IMarketDataProvider inner, RateLimiter limiter, ILogger<RateLimitedMarketDataProvider> logger) : IMarketDataProvider
{
    public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.GetQuoteAsync(symbol, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.SearchStocksAsync(query, cancellationToken), cancellationToken);

    public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.GetHistoryAsync(request, cancellationToken), cancellationToken);

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!lease.IsAcquired)
        {
            logger.LogWarning("Market data provider request rejected by the local rate limit.");
            throw new MarketDataRateLimitException();
        }
        // Failed provider attempts still consume their window permit; the lease is always disposed.
        return await call().ConfigureAwait(false);
    }
}
