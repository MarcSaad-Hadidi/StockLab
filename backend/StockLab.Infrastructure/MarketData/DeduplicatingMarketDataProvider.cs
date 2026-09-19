using System.Collections.Concurrent;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.MarketData;

/// <summary>Shares in-progress calls and cancels work when its last caller leaves.</summary>
public sealed class DeduplicatingMarketDataProvider(IMarketDataProvider inner) : IMarketDataProvider
{
    private sealed class Flight<T>
    {
        public readonly TaskCompletionSource<T> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly CancellationTokenSource Cancellation = new();
        public int Callers;
    }
    private readonly ConcurrentDictionary<string, Flight<StockQuote?>> quotes = new();
    private readonly ConcurrentDictionary<string, Flight<IReadOnlyList<StockSearchResult>>> searches = new();
    private readonly ConcurrentDictionary<StockHistoryRequest, Flight<StockHistory?>> histories = new();
    internal int InFlightCount => quotes.Count + searches.Count + histories.Count;

    public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(symbol);
        return JoinAsync(quotes, normalized, token => inner.GetQuoteAsync(normalized, token), cancellationToken);
    }

    public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(query);
        return JoinAsync(searches, normalized, token => inner.SearchStocksAsync(normalized, token), cancellationToken);
    }

    public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var normalized = request with { Symbol = Normalize(request.Symbol) };
        request.Validate();
        return JoinAsync(histories, normalized, token => inner.GetHistoryAsync(normalized, token), cancellationToken);
    }

    private static async Task<T> JoinAsync<TKey, T>(ConcurrentDictionary<TKey, Flight<T>> flights,
        TKey key, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken) where TKey : notnull
    {
        Flight<T> flight;
        bool owner;
        lock (flights)
        {
            owner = !flights.TryGetValue(key, out flight!);
            if (owner) flights[key] = flight = new Flight<T>();
            flight.Callers++;
        }
        if (owner) _ = CompleteAsync(flights, key, flight, load);
        try { return await flight.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            lock (flights)
            {
                if (--flight.Callers == 0 && !flight.Completion.Task.IsCompleted)
                {
                    // New callers must not join a cancelled queue entry, even while it unwinds.
                    flights.TryRemove(new KeyValuePair<TKey, Flight<T>>(key, flight));
                    flight.Cancellation.Cancel();
                }
            }
        }
    }

    private static async Task CompleteAsync<TKey, T>(ConcurrentDictionary<TKey, Flight<T>> flights,
        TKey key, Flight<T> flight, Func<CancellationToken, Task<T>> load) where TKey : notnull
    {
        T result = default!;
        Exception? failure = null;
        try
        {
            result = await load(flight.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception) { failure = exception; }
        lock (flights)
        {
            // Remove only this flight before notifying waiters, including synchronous failures.
            flights.TryRemove(new KeyValuePair<TKey, Flight<T>>(key, flight));
            if (failure is OperationCanceledException cancelled)
                flight.Completion.TrySetCanceled(cancelled.CancellationToken);
            else if (failure is not null)
            {
                flight.Completion.TrySetException(failure);
                _ = flight.Completion.Task.Exception;
            }
            else flight.Completion.TrySetResult(result);
            flight.Cancellation.Dispose();
        }
    }

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToUpperInvariant();
    }
}
