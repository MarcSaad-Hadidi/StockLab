using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.MarketData;

/// <summary>Caches completed responses only. Concurrent misses may independently call the provider.</summary>
public sealed class CachingMarketDataProvider : IMarketDataProvider
{
    private readonly IMarketDataProvider inner;
    private readonly IMemoryCache cache;
    private readonly TimeSpan quoteTtl;
    private readonly TimeSpan searchTtl;
    private readonly TimeSpan historyTtl;
    private readonly object keyNamespace = new();

    public CachingMarketDataProvider(IMarketDataProvider inner, IMemoryCache cache,
        IOptions<MarketDataCacheOptions> options)
    {
        this.inner = inner;
        this.cache = cache;
        var settings = options.Value;
        if (!settings.HasValidTtls())
            throw new OptionsValidationException(MarketDataCacheOptions.SectionName,
                typeof(MarketDataCacheOptions), ["Cache TTLs must be positive and at most 365 days."]);
        quoteTtl = settings.QuoteTtl;
        searchTtl = settings.SearchTtl;
        historyTtl = settings.HistoryTtl;
    }

    public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(symbol);
        return GetOrLoadAsync((keyNamespace, "quote", normalized), normalized.Length, quoteTtl,
            () => inner.GetQuoteAsync(normalized, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(query);
        // Snapshot collections: callers and providers must not mutate a cached result.
        return (await GetOrLoadAsync<IReadOnlyList<StockSearchResult>>(
            (keyNamespace, "search", normalized), normalized.Length, searchTtl, async () =>
                Array.AsReadOnly((await inner.SearchStocksAsync(normalized, cancellationToken)).ToArray()),
            cancellationToken))!;
    }

    public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var normalized = request with { Symbol = Normalize(request.Symbol) };
        // Check contract preconditions before lookup, including on cache hits.
        request.Validate();

        return GetOrLoadAsync((keyNamespace, "history", normalized), normalized.Symbol.Length, historyTtl, async () =>
        {
            var history = await inner.GetHistoryAsync(normalized, cancellationToken);
            return history is null ? null : history with { Bars = Array.AsReadOnly(history.Bars.ToArray()) };
        }, cancellationToken);
    }

    private async Task<T?> GetOrLoadAsync<T>(object key, int keyLength, TimeSpan ttl, Func<Task<T?>> load,
        CancellationToken cancellationToken) where T : class
    {
        if (cache.TryGetValue<T>(key, out var cached))
            return cached;

        var result = await load(); // Exceptions/cancellation escape without creating entries.
        cancellationToken.ThrowIfCancellationRequested();
        // Unknown symbols are not negatively cached. Valid empty collections are cached.
        if (result is not null)
            cache.Set(key, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
                Size = 512L + 2L * keyLength + EstimatePayloadSize(result)
            });
        return result;
    }

    // Budget units approximate retained bytes, including UTF-16 strings and collection elements.
    // This is a bounded accounting budget, not an exact measurement of the CLR heap.
    private static long EstimatePayloadSize(object result) => result switch
    {
        StockQuote quote => 256L + TextSize(quote.Symbol) + TextSize(quote.Currency) + TextSize(quote.Name) + TextSize(quote.Exchange) + TextSize(quote.FiftyTwoWeek?.Range),
        IReadOnlyList<StockSearchResult> stocks => stocks.Sum(stock =>
            128L + TextSize(stock.Symbol) + TextSize(stock.CompanyName) + TextSize(stock.Exchange) + TextSize(stock.Currency)),
        StockHistory history => 128L + TextSize(history.Symbol) + TextSize(history.Currency) + 128L * history.Bars.Count,
        _ => throw new InvalidOperationException("Unsupported cache result type.")
    };

    private static long TextSize(string? value) => value is null ? 0 : 24L + 2L * value.Length;
    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToUpperInvariant();
    }
}
