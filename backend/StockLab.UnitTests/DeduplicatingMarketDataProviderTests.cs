using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.UnitTests;

public sealed class DeduplicatingMarketDataProviderTests
{
    private readonly BlockingProvider inner = new();
    private static readonly StockHistoryRequest History = new("AAPL",
        DateTimeOffset.Parse("2026-08-24T13:30:00Z"), DateTimeOffset.Parse("2026-08-29T13:30:00Z"), StockHistoryInterval.Day);

    private static async Task<object?> Call(IMarketDataProvider provider, string operation,
        string value = "AAPL", CancellationToken token = default) => operation switch
    {
        "quote" => await provider.GetQuoteAsync(value, token),
        "search" => await provider.SearchStocksAsync(value, token),
        "empty-history" => await provider.GetHistoryAsync(History with { FromUtc = History.ToUtc, ToUtc = History.ToUtc.AddDays(1) }, token),
        _ => await provider.GetHistoryAsync(History with { Symbol = value }, token)
    };

    [Theory]
    [InlineData("quote", "AAPL")]
    [InlineData("search", "apple")]
    [InlineData("history", "AAPL")]
    [InlineData("quote", "INVALID")]
    [InlineData("history", "INVALID")]
    [InlineData("search", "zzzzzz")]
    [InlineData("empty-history", "AAPL")]
    public async Task Ten_active_callers_share_results_and_cleanup(string operation, string value)
    {
        var dedup = new DeduplicatingMarketDataProvider(inner);
        var tasks = Enumerable.Range(0, 10).Select(_ => Call(dedup, operation, value)).ToArray();
        Assert.All(tasks, task => Assert.False(task.IsCompleted));
        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, dedup.InFlightCount);
        inner.Release();
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, result => Assert.Same(results[0], result));
        if (value == "INVALID") Assert.Null(results[0]);
        else if (value == "zzzzzz") Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<StockSearchResult>>(results[0]));
        else if (operation == "empty-history") Assert.Empty(Assert.IsType<StockHistory>(results[0]).Bars);
        else if (operation == "quote") Assert.Equal("AAPL", Assert.IsType<StockQuote>(results[0]).Symbol);
        else if (operation == "search") Assert.Equal("AAPL", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<StockSearchResult>>(results[0])).Symbol);
        else Assert.Equal(5, Assert.IsType<StockHistory>(results[0]).Bars.Count);
        Assert.Equal(0, dedup.InFlightCount);
        var retry = Call(dedup, operation, value);
        Assert.Equal(2, inner.Calls);
        inner.Release();
        await retry;
        Assert.Equal(0, dedup.InFlightCount);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Equivalent_normalized_values_share_a_flight(string operation)
    {
        var dedup = new DeduplicatingMarketDataProvider(inner);
        var tasks = new[] { "AAPL", "aapl", " AAPL " }.Select(value => Call(dedup, operation, value)).ToArray();
        Assert.Equal(1, inner.Calls);
        inner.Release();
        await Task.WhenAll(tasks);
        Assert.Equal(0, dedup.InFlightCount);
    }

    [Fact]
    public async Task Different_keys_and_operations_are_independent()
    {
        var dedup = new DeduplicatingMarketDataProvider(inner);
        Task[] tasks = [
            dedup.GetQuoteAsync("AAPL"), dedup.GetQuoteAsync("MSFT"),
            dedup.SearchStocksAsync("AAPL"), dedup.SearchStocksAsync("MSFT"),
            dedup.GetHistoryAsync(History),
            dedup.GetHistoryAsync(History with { FromUtc = History.FromUtc.AddDays(1) }),
            dedup.GetHistoryAsync(History with { ToUtc = History.ToUtc.AddDays(1) }),
            dedup.GetHistoryAsync(History with { Interval = StockHistoryInterval.Hour }),
            dedup.GetHistoryAsync(History with { Symbol = "MSFT" })
        ];
        Assert.All(tasks, task => Assert.False(task.IsCompleted));
        Assert.Equal(9, inner.Calls);
        Assert.Equal(9, dedup.InFlightCount);
        inner.Release();
        await Task.WhenAll(tasks);
        Assert.Equal(0, dedup.InFlightCount);
    }

    [Theory]
    [InlineData("quote", false)]
    [InlineData("search", false)]
    [InlineData("history", false)]
    [InlineData("quote", true)]
    [InlineData("search", true)]
    [InlineData("history", true)]
    public async Task Shared_failure_or_cancellation_is_removed_and_retry_succeeds(string operation, bool cancelled)
    {
        var dedup = new DeduplicatingMarketDataProvider(inner);
        var tasks = Enumerable.Range(0, 10).Select(_ => Call(dedup, operation)).ToArray();
        Assert.Equal(1, inner.Calls);
        Exception failure = cancelled ? new OperationCanceledException() : new InvalidOperationException("controlled failure");
        inner.Release(failure);
        foreach (var task in tasks)
        {
            if (cancelled) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            else Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => task));
        }
        Assert.Equal(0, dedup.InFlightCount);
        var retry = Call(dedup, operation);
        Assert.Equal(2, inner.Calls);
        inner.Release();
        Assert.NotNull(await retry);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task First_caller_cancellation_does_not_cancel_other_waiters(string operation)
    {
        var dedup = new DeduplicatingMarketDataProvider(inner);
        using var caller = new CancellationTokenSource();
        var first = Call(dedup, operation, token: caller.Token);
        var second = Call(dedup, operation);
        caller.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.False(second.IsCompleted);
        Assert.All(inner.Tokens, token => Assert.False(token.CanBeCanceled));
        Assert.Equal(1, inner.Calls);
        inner.Release();
        Assert.NotNull(await second);
        Assert.Equal(0, dedup.InFlightCount);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Already_cancelled_caller_never_starts_work(string operation)
    {
        var dedup = new DeduplicatingMarketDataProvider(inner);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(dedup, operation, token: new CancellationToken(true)));
        Assert.Equal(0, inner.Calls);
        Assert.Equal(0, dedup.InFlightCount);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Cache_wraps_dedup_across_bursts_and_expiration(string operation)
    {
        var clock = new TestClock();
        using var memory = new MemoryCache(new MemoryCacheOptions { Clock = clock, SizeLimit = 8 * 1024 * 1024 });
        var dedup = new DeduplicatingMarketDataProvider(inner);
        var cache = new CachingMarketDataProvider(dedup, memory, Options.Create(new MarketDataCacheOptions
        {
            QuoteTtl = TimeSpan.FromSeconds(10), SearchTtl = TimeSpan.FromSeconds(10), HistoryTtl = TimeSpan.FromSeconds(10)
        }));
        var burst = Enumerable.Range(0, 10).Select(_ => Call(cache, operation)).ToArray();
        Assert.Equal(1, inner.Calls);
        Assert.All(burst, task => Assert.False(task.IsCompleted));
        inner.Release();
        await Task.WhenAll(burst);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Call(cache, operation)));
        Assert.Equal(1, inner.Calls);
        Assert.Equal(0, dedup.InFlightCount);
        clock.UtcNow += TimeSpan.FromSeconds(10);
        var expiredBurst = Enumerable.Range(0, 10).Select(_ => Call(cache, operation)).ToArray();
        Assert.Equal(2, inner.Calls);
        inner.Release();
        await Task.WhenAll(expiredBurst);
        Assert.Equal(0, dedup.InFlightCount);
    }

    [Fact]
    public async Task Thread_pool_contention_starts_one_provider_call()
    {
        var dedup = new DeduplicatingMarketDataProvider(inner);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            var pending = dedup.GetQuoteAsync("AAPL");
            if (Interlocked.Increment(ref count) == 10) joined.SetResult();
            return await pending;
        })).ToArray();
        start.SetResult();
        await joined.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, inner.Calls);
        inner.Release();
        await Task.WhenAll(tasks);
        Assert.Equal(0, dedup.InFlightCount);
    }

    [Fact]
    public async Task Invalid_history_cannot_join_an_equivalent_utc_flight()
    {
        var dedup = new DeduplicatingMarketDataProvider(inner);
        var valid = dedup.GetHistoryAsync(History);
        await Assert.ThrowsAsync<ArgumentException>(() => dedup.GetHistoryAsync(History with { FromUtc = History.FromUtc.ToOffset(TimeSpan.FromHours(2)) }));
        Assert.Equal(1, inner.Calls);
        inner.Release();
        await valid;
        Assert.Equal(0, dedup.InFlightCount);
    }

    [Fact]
    public async Task Synchronous_completion_and_failure_leave_no_flights()
    {
        var dedup = new DeduplicatingMarketDataProvider(new MockMarketDataProvider());
        Assert.NotNull(await dedup.GetQuoteAsync("AAPL"));
        Assert.Equal(0, dedup.InFlightCount);
        await Assert.ThrowsAsync<NotSupportedException>(() => dedup.GetHistoryAsync(History with { Interval = StockHistoryInterval.Hour }));
        Assert.Equal(0, dedup.InFlightCount);
        Assert.NotNull(await dedup.GetHistoryAsync(History));
        Assert.Equal(0, dedup.InFlightCount);
    }
    private sealed class TestClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
    }

    private sealed class BlockingProvider : IMarketDataProvider
    {
        private readonly MockMarketDataProvider mock = new();
        private readonly ConcurrentQueue<TaskCompletionSource> gates = new();
        public ConcurrentQueue<CancellationToken> Tokens { get; } = new();
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        private Task Block(CancellationToken token)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Tokens.Enqueue(token);
            gates.Enqueue(gate);
            Interlocked.Increment(ref calls);
            return gate.Task;
        }
        public void Release(Exception? failure = null)
        {
            while (gates.TryDequeue(out var gate))
            {
                if (failure is null) gate.SetResult();
                else gate.SetException(failure);
            }
        }
        public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            await Block(cancellationToken);
            return await mock.GetQuoteAsync(symbol);
        }
        public async Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
        {
            await Block(cancellationToken);
            return await mock.SearchStocksAsync(query);
        }
        public async Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
        {
            await Block(cancellationToken);
            // Permit alternate intervals in this fake to isolate key behavior from mock capabilities.
            var history = await mock.GetHistoryAsync(request with { Interval = StockHistoryInterval.Day });
            return history is null ? null : history with { Interval = request.Interval };
        }
    }
}
