using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.UnitTests;

public sealed class CachingMarketDataProviderTests : IDisposable
{
    private readonly TestClock clock = new();
    private readonly CountingProvider provider = new();
    private readonly MemoryCache memory;
    private readonly CachingMarketDataProvider cached;
    private static readonly StockHistoryRequest Request = new("AAPL",
        DateTimeOffset.Parse("2026-08-24T13:30:00Z"), DateTimeOffset.Parse("2026-08-29T13:30:00Z"), StockHistoryInterval.Day);

    public CachingMarketDataProviderTests()
    {
        memory = new MemoryCache(new MemoryCacheOptions { Clock = clock, SizeLimit = 8 * 1024 * 1024 });
        cached = new(provider, memory, Options.Create(new MarketDataCacheOptions
        {
            QuoteTtl = TimeSpan.FromSeconds(10), SearchTtl = TimeSpan.FromSeconds(20), HistoryTtl = TimeSpan.FromSeconds(30)
        }));
    }

    private async Task<object?> Call(string operation, CancellationToken token = default) => operation switch
    {
        "quote" => await cached.GetQuoteAsync("AAPL", token),
        "search" => await cached.SearchStocksAsync("apple", token),
        _ => await cached.GetHistoryAsync(Request, token)
    };

    [Theory]
    [InlineData("quote", 10)]
    [InlineData("search", 20)]
    [InlineData("history", 30)]
    public async Task Miss_hit_and_absolute_expiration_use_independent_ttls(string operation, int ttl)
    {
        var first = await Call(operation);
        Assert.NotNull(first);
        Assert.Equal(1, provider.Calls);
        clock.Advance(TimeSpan.FromSeconds(ttl - 1));
        Assert.Same(first, await Call(operation));
        Assert.Equal(1, provider.Calls);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Call(operation);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Equivalent_symbols_and_search_terms_share_entries()
    {
        await cached.GetQuoteAsync("AAPL");
        await cached.GetQuoteAsync("aapl");
        await cached.GetQuoteAsync(" AAPL ");
        await cached.SearchStocksAsync("apple");
        await cached.SearchStocksAsync(" APPLE ");
        await cached.GetHistoryAsync(Request);
        await cached.GetHistoryAsync(Request with { Symbol = " aapl " });
        Assert.Equal(3, provider.Calls);
    }

    [Fact]
    public async Task Different_history_keys_do_not_collide()
    {
        await cached.GetHistoryAsync(Request);
        await cached.GetHistoryAsync(Request with { FromUtc = Request.FromUtc.AddDays(1) });
        await cached.GetHistoryAsync(Request with { ToUtc = Request.ToUtc.AddDays(1) });
        await cached.GetHistoryAsync(Request with { Symbol = "MSFT" });
        // This fake supports Hour to test cache keys independently of the mock's capabilities.
        await cached.GetHistoryAsync(Request with { Interval = StockHistoryInterval.Hour });
        Assert.Equal(5, provider.Calls);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Failure_is_not_cached_and_next_call_retries(string operation)
    {
        provider.Failure = new InvalidOperationException("test failure");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Call(operation));
        provider.Failure = null;
        Assert.NotNull(await Call(operation));
        await Call(operation);
        Assert.Equal(2, provider.Calls);
    }

    [Theory]
    [InlineData("quote", false)]
    [InlineData("search", false)]
    [InlineData("history", false)]
    [InlineData("quote", true)]
    [InlineData("search", true)]
    [InlineData("history", true)]
    public async Task Already_cancelled_requests_fail_on_miss_and_hit(string operation, bool warm)
    {
        if (warm) await Call(operation);
        using var source = new CancellationTokenSource();
        source.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(operation, source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.Equal(warm ? 1 : 0, provider.Calls);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Miss_forwards_token_and_cancelled_result_is_not_cached(string operation)
    {
        using var source = new CancellationTokenSource();
        provider.BeforeReturn = source.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(operation, source.Token));
        Assert.Equal(source.Token, provider.LastToken);
        provider.BeforeReturn = null;
        await Call(operation);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Unknown_symbols_are_not_cached_but_empty_results_are()
    {
        for (var i = 0; i < 2; i++)
        {
            Assert.Null(await cached.GetQuoteAsync("INVALID"));
            Assert.Null(await cached.GetHistoryAsync(Request with { Symbol = "INVALID" }));
        }
        Assert.Equal(4, provider.Calls);
        for (var i = 0; i < 2; i++)
        {
            Assert.Empty(await cached.SearchStocksAsync("zzzzzz"));
            var empty = await cached.GetHistoryAsync(Request with { FromUtc = Request.ToUtc, ToUtc = Request.ToUtc.AddDays(1) });
            Assert.NotNull(empty);
            Assert.Empty(empty.Bars);
        }
        Assert.Equal(6, provider.Calls);
    }

    [Fact]
    public async Task Invalid_history_cannot_hit_equivalent_utc_entry()
    {
        await cached.GetHistoryAsync(Request);
        await Assert.ThrowsAsync<ArgumentException>(() => cached.GetHistoryAsync(Request with { FromUtc = Request.FromUtc.ToOffset(TimeSpan.FromHours(2)) }));
        await Assert.ThrowsAsync<ArgumentException>(() => cached.GetHistoryAsync(Request with { ToUtc = Request.FromUtc }));
        await Assert.ThrowsAsync<ArgumentException>(() => cached.GetHistoryAsync(Request with { Interval = (StockHistoryInterval)999 }));
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void Invalid_ttls_fail_explicitly(int days)
    {
        var invalid = TimeSpan.FromDays(days);
        Assert.Throws<OptionsValidationException>(() => new CachingMarketDataProvider(provider, memory,
            Options.Create(new MarketDataCacheOptions { QuoteTtl = invalid })));
        Assert.Throws<OptionsValidationException>(() => new CachingMarketDataProvider(provider, memory,
            Options.Create(new MarketDataCacheOptions { SearchTtl = invalid })));
        Assert.Throws<OptionsValidationException>(() => new CachingMarketDataProvider(provider, memory,
            Options.Create(new MarketDataCacheOptions { HistoryTtl = invalid })));
    }

    [Fact]
    public async Task Cache_namespace_isolates_different_decorator_instances()
    {
        await cached.GetQuoteAsync("AAPL");
        var other = new CountingProvider();
        var second = new CachingMarketDataProvider(other, memory, Options.Create(new MarketDataCacheOptions()));
        await second.GetQuoteAsync("AAPL");
        Assert.Equal(1, other.Calls);
    }

    [Fact]
    public async Task Distinct_empty_searches_cannot_grow_cache_beyond_budget()
    {
        using var bounded = new MemoryCache(new MemoryCacheOptions { SizeLimit = 4096 });
        var cache = new CachingMarketDataProvider(provider, bounded, Options.Create(new MarketDataCacheOptions()));
        for (var i = 0; i < 100; i++)
        {
            Assert.Empty(await cache.SearchStocksAsync($"no-match-{i}"));
            // Every retained entry accounts for at least 512 units, even with no results.
            Assert.InRange(bounded.Count, 0, 8);
        }
        Assert.Equal(100, provider.Calls);
    }

    [Fact]
    public async Task Oversized_key_is_served_but_not_retained()
    {
        using var bounded = new MemoryCache(new MemoryCacheOptions { SizeLimit = 4096 });
        var cache = new CachingMarketDataProvider(provider, bounded, Options.Create(new MarketDataCacheOptions()));
        var query = new string('z', 5000);
        Assert.Empty(await cache.SearchStocksAsync(query));
        Assert.Empty(await cache.SearchStocksAsync(query));
        Assert.Equal(0, bounded.Count);
        Assert.Equal(2, provider.Calls);
    }
    public void Dispose() => memory.Dispose();

    // MemoryCache's native clock hook makes expiration tests deterministic without delays.
    private sealed class TestClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class CountingProvider : IMarketDataProvider
    {
        private readonly MockMarketDataProvider mock = new();
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Action? BeforeReturn { get; set; }
        public CancellationToken LastToken { get; private set; }
        private void Count(CancellationToken token)
        {
            Calls++;
            LastToken = token;
            if (Failure is not null) throw Failure;
            BeforeReturn?.Invoke();
        }
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            Count(cancellationToken);
            return mock.GetQuoteAsync(symbol);
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
        {
            Count(cancellationToken);
            return mock.SearchStocksAsync(query);
        }
        public async Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Count(cancellationToken);
            var history = await mock.GetHistoryAsync(request with { Interval = StockHistoryInterval.Day });
            return history is null ? null : history with { Interval = request.Interval };
        }
    }
}
