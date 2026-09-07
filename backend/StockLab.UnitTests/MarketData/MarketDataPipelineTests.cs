using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockLab.Api.Controllers;
using StockLab.Api.Middleware;
using StockLab.Api.Validation;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.UnitTests.MarketData;

// Complements the existing burst/hit/expiration tests; every case owns its whole pipeline.
public sealed class MarketDataPipelineTests
{
    private static readonly StockHistoryRequest History = new("AAPL",
        DateTimeOffset.Parse("2026-08-24T13:30:00Z"), DateTimeOffset.Parse("2026-08-29T13:30:00Z"), StockHistoryInterval.Day);

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Shared_provider_failure_consumes_one_permit_but_is_not_cached(string operation)
    {
        using var pipeline = new Pipeline();
        pipeline.Inner.Block();
        var burst = pipeline.Burst(operation);
        Assert.All(burst, task => Assert.False(task.IsCompleted));
        Assert.Equal(1, pipeline.Inner.Calls);
        Assert.Equal(7, pipeline.Permits);
        var failure = new InvalidOperationException("controlled failure");
        pipeline.Inner.Release(failure);
        foreach (var task in burst)
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => task));
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
        Assert.Equal(0, pipeline.Memory.Count);
        pipeline.Inner.Block();
        var retry = pipeline.Burst(operation);
        Assert.Equal(2, pipeline.Inner.Calls);
        Assert.Equal(6, pipeline.Permits);
        pipeline.Inner.Release();
        Assert.All(await Task.WhenAll(retry), result => Assert.NotNull(result));
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Rejection_is_not_cached_and_renewal_allows_retry(string operation)
    {
        using var pipeline = new Pipeline(1);
        using var spent = pipeline.Limiter.AttemptAcquire(1);
        Assert.True(spent.IsAcquired);
        await Assert.ThrowsAsync<MarketDataRateLimitException>(() => pipeline.Call(operation));
        Assert.Equal(0, pipeline.Inner.Calls);
        Assert.Equal(0, pipeline.Memory.Count);
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
        Assert.True(pipeline.Limiter.TryReplenish());
        pipeline.Inner.Block();
        var retry = pipeline.Burst(operation);
        Assert.Equal(1, pipeline.Inner.Calls);
        Assert.Equal(0, pipeline.Permits);
        pipeline.Inner.Release();
        await Task.WhenAll(retry);
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
    }

    [Theory]
    [InlineData("null-quote", false)]
    [InlineData("null-history", false)]
    [InlineData("empty-search", true)]
    [InlineData("empty-history", true)]
    public async Task Shared_null_and_empty_results_keep_existing_cache_rules(string operation, bool cacheable)
    {
        using var pipeline = new Pipeline();
        pipeline.Inner.Block();
        var first = pipeline.Burst(operation);
        Assert.Equal(1, pipeline.Inner.Calls);
        Assert.All(first, task => Assert.False(task.IsCompleted));
        pipeline.Inner.Release();
        AssertResults(await Task.WhenAll(first), operation);
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
        pipeline.Inner.Block();
        var next = pipeline.Burst(operation);
        Assert.Equal(cacheable ? 1 : 2, pipeline.Inner.Calls);
        Assert.Equal(cacheable ? 7 : 6, pipeline.Permits);
        if (!cacheable) Assert.All(next, task => Assert.False(task.IsCompleted));
        pipeline.Inner.Release();
        AssertResults(await Task.WhenAll(next), operation);
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
    }

    [Fact]
    public async Task Distinct_quotes_and_history_ranges_spend_separate_permits()
    {
        using var pipeline = new Pipeline();
        pipeline.Inner.Block();
        var quotes = new[] { "AAPL", "MSFT", "NVDA" }.Select(symbol => pipeline.Cache.GetQuoteAsync(symbol)).ToArray();
        var full = pipeline.Cache.GetHistoryAsync(History);
        var partial = pipeline.Cache.GetHistoryAsync(History with { FromUtc = History.FromUtc.AddDays(1), ToUtc = History.FromUtc.AddDays(3) });
        Assert.Equal(5, pipeline.Inner.Calls);
        Assert.Equal(3, pipeline.Permits);
        pipeline.Inner.Release();
        Assert.Equal(new[] { "AAPL", "MSFT", "NVDA" }, (await Task.WhenAll(quotes)).Select(quote => quote!.Symbol));
        Assert.Equal(5, (await full)!.Bars.Count);
        Assert.Equal(2, (await partial)!.Bars.Count);
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Caller_cancellation_does_not_poison_shared_work_or_cached_success(string operation)
    {
        using var pipeline = new Pipeline();
        pipeline.Inner.Block();
        using var source = new CancellationTokenSource();
        var cancelled = pipeline.Call(operation, source.Token);
        var survivor = pipeline.Call(operation);
        source.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.False(survivor.IsCompleted);
        Assert.Equal(0, pipeline.Memory.Count);
        Assert.Equal(1, pipeline.Inner.Calls);
        pipeline.Inner.Release();
        var success = await survivor;
        Assert.NotNull(success);
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.Call(operation, source.Token));
        Assert.Same(success, await pipeline.Call(operation));
        Assert.Equal(1, pipeline.Inner.Calls);
        Assert.Equal(7, pipeline.Permits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_controller_pipeline_returns_safe_429_or_500(bool providerFailure)
    {
        using var pipeline = new Pipeline(1);
        if (providerFailure)
        {
            pipeline.Inner.Block();
            pipeline.Inner.Release(new InvalidOperationException("INTERNAL-MARKER C:\\private\\file.cs password=TEST-ONLY-MARKER"));
        }
        else
        {
            using var spent = pipeline.Limiter.AttemptAcquire(1);
            Assert.True(spent.IsAcquired);
        }
        using var host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IMarketDataProvider>(pipeline.Cache);
                services.AddControllers().AddApplicationPart(typeof(StocksController).Assembly)
                    .ConfigureApiBehaviorOptions(ApiValidation.Configure);
            })
            .Configure(app =>
            {
                app.UseMiddleware<ExceptionHandlingMiddleware>();
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).Start();
        using var client = host.GetTestClient(); // In-memory TestServer transport, no network socket.
        using var response = await client.GetAsync("/api/stocks/AAPL/quote");
        Assert.Equal(providerFailure ? HttpStatusCode.InternalServerError : HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.Equal(providerFailure ? "internal_server_error" : "market_data_rate_limited", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(providerFailure ? "An unexpected error occurred." : "Market data requests are temporarily rate limited.", json.RootElement.GetProperty("message").GetString());
        foreach (var forbidden in new[] { "INTERNAL-MARKER", "private", "password", "TEST-ONLY-MARKER", "stackTrace", "InvalidOperationException" })
            Assert.DoesNotContain(forbidden, body);
        Assert.False(response.Headers.Contains("Retry-After"));
        Assert.Equal(providerFailure ? 1 : 0, pipeline.Inner.Calls);
        Assert.Equal(0, pipeline.Memory.Count);
        Assert.Equal(0, pipeline.Dedup.InFlightCount);
    }

    private static void AssertResults(object?[] results, string operation)
    {
        Assert.All(results, result =>
        {
            if (operation.StartsWith("null-", StringComparison.Ordinal)) Assert.Null(result);
            else if (operation == "empty-search") Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<StockSearchResult>>(result));
            else Assert.Empty(Assert.IsType<StockHistory>(result).Bars);
        });
    }

    private sealed class Pipeline : IDisposable
    {
        public ControlledProvider Inner { get; } = new();
        public MemoryCache Memory { get; } = new(new MemoryCacheOptions { SizeLimit = 8 * 1024 * 1024 });
        public FixedWindowRateLimiter Limiter { get; }
        public DeduplicatingMarketDataProvider Dedup { get; }
        public CachingMarketDataProvider Cache { get; }
        public long Permits => Limiter.GetStatistics()!.CurrentAvailablePermits;
        public Pipeline(int permits = 8)
        {
            Limiter = new(new FixedWindowRateLimiterOptions { PermitLimit = permits, QueueLimit = 0,
                Window = TimeSpan.FromTicks(1), AutoReplenishment = false, QueueProcessingOrder = QueueProcessingOrder.OldestFirst });
            Dedup = new(new RateLimitedMarketDataProvider(Inner, Limiter, NullLogger<RateLimitedMarketDataProvider>.Instance));
            Cache = new(Dedup, Memory, Options.Create(new MarketDataCacheOptions()));
        }
        public async Task<object?> Call(string operation, CancellationToken token = default) => operation switch
        {
            "quote" => await Cache.GetQuoteAsync("AAPL", token),
            "search" => await Cache.SearchStocksAsync("apple", token),
            "history" => await Cache.GetHistoryAsync(History, token),
            "null-quote" => await Cache.GetQuoteAsync("INVALID", token),
            "null-history" => await Cache.GetHistoryAsync(History with { Symbol = "INVALID" }, token),
            "empty-search" => await Cache.SearchStocksAsync("zzzzzz", token),
            _ => await Cache.GetHistoryAsync(History with { FromUtc = History.ToUtc, ToUtc = History.ToUtc.AddDays(1) }, token)
        };
        public Task<object?>[] Burst(string operation) => Enumerable.Range(0, 10).Select(_ => Call(operation)).ToArray();
        public void Dispose() { Limiter.Dispose(); Memory.Dispose(); }
    }

    private sealed class ControlledProvider : IMarketDataProvider
    {
        private readonly MockMarketDataProvider mock = new();
        private TaskCompletionSource? gate;
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public void Block() => gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release(Exception? failure = null)
        {
            if (failure is null) gate!.SetResult();
            else gate!.SetException(failure);
        }
        private async Task Enter()
        {
            Interlocked.Increment(ref calls);
            if (gate is not null) await gate.Task;
        }
        public async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        { await Enter(); return await mock.GetQuoteAsync(symbol, cancellationToken); }
        public async Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
        { await Enter(); return await mock.SearchStocksAsync(query, cancellationToken); }
        public async Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
        { await Enter(); return await mock.GetHistoryAsync(request, cancellationToken); }
    }
}
