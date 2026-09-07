using System.Threading.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.UnitTests;

public sealed class RateLimitedMarketDataProviderTests
{
    private readonly CountingProvider inner = new();
    private readonly RecordingLogger logger = new();
    private static readonly StockHistoryRequest History = new("AAPL",
        DateTimeOffset.Parse("2026-08-24T13:30:00Z"), DateTimeOffset.Parse("2026-08-29T13:30:00Z"), StockHistoryInterval.Day);

    private static FixedWindowRateLimiter Limiter(int permits, int queue = 0) => new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = permits, QueueLimit = queue, QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        // No timer: only explicit TryReplenish advances the native limiter in these tests.
        AutoReplenishment = false, Window = TimeSpan.FromTicks(1)
    });
    private RateLimitedMarketDataProvider Wrap(RateLimiter limiter) => new(inner, limiter, logger);
    private static async Task<object?> Call(IMarketDataProvider provider, string operation, CancellationToken token = default) => operation switch
    {
        "quote" => await provider.GetQuoteAsync("AAPL", token),
        "search" => await provider.SearchStocksAsync("apple", token),
        "null-quote" => await provider.GetQuoteAsync("INVALID", token),
        "null-history" => await provider.GetHistoryAsync(History with { Symbol = "INVALID" }, token),
        "empty-search" => await provider.SearchStocksAsync("zzzzzz", token),
        "empty-history" => await provider.GetHistoryAsync(History with { FromUtc = History.ToUtc, ToUtc = History.ToUtc.AddDays(1) }, token),
        _ => await provider.GetHistoryAsync(History, token)
    };

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    [InlineData("null-quote")]
    [InlineData("null-history")]
    [InlineData("empty-search")]
    [InlineData("empty-history")]
    public async Task Each_attempt_consumes_a_permit_and_exhaustion_rejects(string operation)
    {
        using var limiter = Limiter(1);
        var provider = Wrap(limiter);
        await Call(provider, operation);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(0, limiter.GetStatistics()!.CurrentAvailablePermits);
        var error = await Assert.ThrowsAsync<MarketDataRateLimitException>(() => Call(provider, operation));
        Assert.Equal("Market data requests are temporarily rate limited.", error.Message);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Messages).Level);
        Assert.DoesNotContain("AAPL", logger.Messages[0].Text);
    }

    [Fact]
    public async Task Quote_search_history_and_distinct_symbols_share_one_budget()
    {
        using var limiter = Limiter(3);
        var provider = Wrap(limiter);
        await provider.GetQuoteAsync("AAPL");
        await provider.SearchStocksAsync("apple");
        await provider.GetHistoryAsync(History with { Symbol = "MSFT" });
        await Assert.ThrowsAsync<MarketDataRateLimitException>(() => provider.GetQuoteAsync("NVDA"));
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Bounded_queue_rejects_overflow_and_manual_replenishment_releases_waiter()
    {
        using var limiter = Limiter(1, 1);
        var provider = Wrap(limiter);
        await provider.GetQuoteAsync("AAPL");
        var queued = provider.GetQuoteAsync("MSFT");
        Assert.False(queued.IsCompleted);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentQueuedCount);
        await Assert.ThrowsAsync<MarketDataRateLimitException>(() => provider.GetQuoteAsync("NVDA"));
        Assert.Equal(1, inner.Calls);
        Assert.True(limiter.TryReplenish());
        Assert.Equal("MSFT", (await queued.WaitAsync(TimeSpan.FromSeconds(5)))!.Symbol);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(0, limiter.GetStatistics()!.CurrentQueuedCount);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Cancellation_while_queued_does_not_call_provider_or_log_rejection(string operation)
    {
        using var limiter = Limiter(1, 1);
        var provider = Wrap(limiter);
        await Call(provider, operation);
        using var source = new CancellationTokenSource();
        var queued = Call(provider, operation, source.Token);
        Assert.False(queued.IsCompleted);
        source.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(logger.Messages);
        Assert.True(limiter.TryReplenish());
        await Call(provider, operation);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Already_cancelled_request_consumes_nothing_and_token_is_forwarded()
    {
        using var limiter = Limiter(1);
        var provider = Wrap(limiter);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetQuoteAsync("AAPL", new CancellationToken(true)));
        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
        Assert.Equal(0, inner.Calls);
        using var source = new CancellationTokenSource();
        await provider.GetQuoteAsync("AAPL", source.Token);
        Assert.Equal(source.Token, inner.LastToken);
    }

    [Fact]
    public async Task Provider_exception_is_unchanged_and_next_permit_remains_usable()
    {
        using var limiter = Limiter(2);
        var provider = Wrap(limiter);
        var failure = new InvalidOperationException("provider failure");
        inner.Failure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetQuoteAsync("AAPL")));
        inner.Failure = null;
        Assert.NotNull(await provider.GetQuoteAsync("MSFT"));
        Assert.Equal(2, inner.Calls);
        Assert.Equal(0, limiter.GetStatistics()!.CurrentAvailablePermits);
        Assert.Empty(logger.Messages);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Cache_and_dedup_consume_only_one_permit_for_a_blocked_burst(string operation)
    {
        using var limiter = Limiter(2);
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 8 * 1024 * 1024 });
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.Gate = gate.Task;
        var provider = new CachingMarketDataProvider(new DeduplicatingMarketDataProvider(Wrap(limiter)),
            memory, Options.Create(new MarketDataCacheOptions()));
        var tasks = Enumerable.Range(0, 10).Select(_ => Call(provider, operation)).ToArray();
        Assert.All(tasks, task => Assert.False(task.IsCompleted));
        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
        gate.SetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Call(provider, operation)));
        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, limiter.GetStatistics()!.CurrentAvailablePermits);
        await provider.GetQuoteAsync("MSFT");
        Assert.Equal(2, inner.Calls);
        Assert.Equal(0, limiter.GetStatistics()!.CurrentAvailablePermits);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(-1, 1, 0)]
    [InlineData(10001, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 86400001, 0)]
    [InlineData(1, 1, -1)]
    [InlineData(1, 1, 1001)]
    public void Invalid_configuration_fails_explicitly(int permits, int milliseconds, int queue)
    {
        var options = new MarketDataRateLimitOptions { PermitLimit = permits, Window = TimeSpan.FromMilliseconds(milliseconds), QueueLimit = queue };
        Assert.False(options.IsValid());
        Assert.Throws<OptionsValidationException>(() => options.CreateLimiter());
    }

    private sealed class CountingProvider : IMarketDataProvider
    {
        private readonly MockMarketDataProvider mock = new();
        public int Calls { get; private set; }
        public Task Gate { get; set; } = Task.CompletedTask;
        public Exception? Failure { get; set; }
        public CancellationToken LastToken { get; private set; }
        private async Task Count(CancellationToken token)
        {
            Calls++;
            LastToken = token;
            if (Failure is not null) throw Failure;
            await Gate;
        }
        public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        { await Count(cancellationToken); return await mock.GetQuoteAsync(symbol, cancellationToken); }
        public async Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
        { await Count(cancellationToken); return await mock.SearchStocksAsync(query, cancellationToken); }
        public async Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
        { await Count(cancellationToken); return await mock.GetHistoryAsync(request, cancellationToken); }
    }
    private sealed class RecordingLogger : ILogger<RateLimitedMarketDataProvider>
    {
        public List<(LogLevel Level, string Text)> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add((level, formatter(state, exception)));
    }
}
