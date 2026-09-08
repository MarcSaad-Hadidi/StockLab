using System.Collections.Concurrent;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.MarketData;

/// <summary>Shares only in-progress calls. Caller cancellation stops its wait, not shared work.</summary>
public sealed class DeduplicatingMarketDataProvider(IMarketDataProvider inner) : IMarketDataProvider
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<StockQuote?>> quotes = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<StockSearchResult>>> searches = new();
    private readonly ConcurrentDictionary<StockHistoryRequest, TaskCompletionSource<StockHistory?>> histories = new();
    internal int InFlightCount => quotes.Count + searches.Count + histories.Count;

    public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(symbol);
        return JoinAsync(quotes, normalized, () => inner.GetQuoteAsync(normalized, CancellationToken.None), cancellationToken);
    }

    public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(query);
        return JoinAsync(searches, normalized, () => inner.SearchStocksAsync(normalized, CancellationToken.None), cancellationToken);
    }

    public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var normalized = request with { Symbol = Normalize(request.Symbol) };
        request.Validate();
        return JoinAsync(histories, normalized, () => inner.GetHistoryAsync(normalized, CancellationToken.None), cancellationToken);
    }

    private static async Task<T> JoinAsync<TKey, T>(ConcurrentDictionary<TKey, TaskCompletionSource<T>> flights,
        TKey key, Func<Task<T>> load, CancellationToken cancellationToken) where TKey : notnull
    {
        var candidate = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = flights.GetOrAdd(key, candidate);
        if (ReferenceEquals(shared, candidate))
            _ = CompleteAsync(flights, key, shared, load);

        return await shared.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CompleteAsync<TKey, T>(ConcurrentDictionary<TKey, TaskCompletionSource<T>> flights,
        TKey key, TaskCompletionSource<T> shared, Func<Task<T>> load) where TKey : notnull
    {
        try
        {
            T result;
            try
            {
                result = await load().ConfigureAwait(false);
            }
            finally
            {
                // Remove only this flight, before notifying waiters, including synchronous failures.
                flights.TryRemove(new KeyValuePair<TKey, TaskCompletionSource<T>>(key, shared));
            }
            shared.TrySetResult(result);
        }
        catch (OperationCanceledException exception)
        {
            shared.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            shared.TrySetException(exception);
            // Observe a fault even if every caller has already abandoned its wait.
            _ = shared.Task.Exception;
        }
    }

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToUpperInvariant();
    }
}
