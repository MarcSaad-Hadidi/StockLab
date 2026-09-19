using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using StockLab.Application.DTOs.MarketEnrichment;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.MarketEnrichment;

/// <summary>Cache and single flight precede the serialized, locally budgeted transport.</summary>
public sealed class AlphaVantageProvider : IMarketEnrichmentProvider, IDisposable
{
    private readonly IHttpClientFactory factory;
    private readonly IConfiguration config;
    private readonly AlphaVantageOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<AlphaVantageProvider>? logger;
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 256 });
    private readonly Dictionary<string, Flight> flights = new();
    private sealed class Flight
    {
        public readonly TaskCompletionSource<object?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly CancellationTokenSource Cancellation = new();
        public int Callers;
    }
    private readonly SemaphoreSlim transport = new(1, 1);
    private DateOnly budgetDay;
    private int used;
    public AlphaVantageProvider(IHttpClientFactory factory, IConfiguration config, IOptions<AlphaVantageOptions> options, TimeProvider clock, ILogger<AlphaVantageProvider>? logger = null)
    {
        this.factory = factory; this.config = config; this.options = options.Value; this.clock = clock; this.logger = logger;
        if (!this.options.IsValid()) throw new OptionsValidationException("AlphaVantage", typeof(AlphaVantageOptions), ["Invalid enrichment options."]);
    }
    public Task<StockFundamentals?> GetFundamentalsAsync(string symbol, CancellationToken cancellationToken = default) =>
        Get<StockFundamentals>("OVERVIEW", symbol, options.OverviewTtl, cancellationToken);
    public async Task<CompanyLogo> GetLogoAsync(string symbol, CancellationToken cancellationToken = default) =>
        (await Get<CompanyLogo>("COMPANY_LOGO", symbol, options.LogoTtl, cancellationToken))!;
    public async Task<StockEarnings> GetEarningsAsync(string symbol, CancellationToken cancellationToken = default) =>
        (await Get<StockEarnings>("EARNINGS_CALENDAR", symbol, options.EarningsTtl, cancellationToken))!;
    public async Task<MarketMovers> GetMoversAsync(CancellationToken cancellationToken = default) =>
        (await Get<MarketMovers>("TOP_GAINERS_LOSERS", null, options.MoversTtl, cancellationToken))!;
    public async Task<StockNews> GetNewsAsync(string symbol, CancellationToken cancellationToken = default) =>
        (await Get<StockNews>("NEWS_SENTIMENT", symbol, options.NewsTtl, cancellationToken))!;

    private sealed record Cached(object? Value, DateTimeOffset Expires, MarketEnrichmentFailure? Failure = null);
    private async Task<T?> Get<T>(string operation, string? symbol, TimeSpan ttl, CancellationToken token) where T : class
    {
        token.ThrowIfCancellationRequested();
        var canonical = symbol is null ? "" : AlphaSymbol.Canonical(symbol);
        var upstream = symbol is null ? null : AlphaSymbol.Resolve(canonical);
        var key = operation + ":" + canonical + (operation == "EARNINGS_CALENDAR" ? ":" + clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "");
        if (cache.TryGetValue<Cached>(key, out var entry) && entry!.Expires > clock.GetUtcNow())
        {
            if (entry.Failure is { } failure) throw Failure(failure);
            return (T?)entry.Value;
        }
        Flight flight;
        bool owner;
        lock (flights)
        {
            owner = !flights.TryGetValue(key, out flight!);
            if (owner) flights.Add(key, flight = new Flight());
            flight.Callers++;
        }
        if (owner) _ = Complete(key, operation, canonical, upstream, ttl, flight);
        try { return (T?)await flight.Completion.Task.WaitAsync(token); }
        finally
        {
            lock (flights)
            {
                if (--flight.Callers == 0 && !flight.Completion.Task.IsCompleted)
                {
                    // Nobody needs this operation anymore. Remove it before cancelling so a
                    // new caller cannot join abandoned work that might still be in the queue.
                    if (flights.TryGetValue(key, out var current) && ReferenceEquals(current, flight)) flights.Remove(key);
                    flight.Cancellation.Cancel();
                }
            }
        }
    }
    private async Task Complete(string key, string operation, string canonical, string? upstream, TimeSpan ttl, Flight flight)
    {
        var shared = flight.Completion;
        try
        {
            // One caller can leave without cancelling remaining joiners.
            var result = await Load(operation, canonical, upstream, flight.Cancellation.Token);
            cache.Set(key, new Cached(result, clock.GetUtcNow() + ttl), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = ttl });
            shared.TrySetResult(result);
        }
        catch (MarketEnrichmentException error)
        {
            logger?.LogWarning("AlphaVantage operation {Operation} failed: {Category}", operation, error.Category);
            // A render/reload storm must not repeatedly spend scarce credits on a known outage.
            var cooldown = error.Category is MarketEnrichmentFailure.PermissionOrEntitlement or MarketEnrichmentFailure.QuotaExceeded
                ? TimeSpan.FromHours(12) : TimeSpan.FromMinutes(1);
            cache.Set(key, new Cached(null, clock.GetUtcNow() + cooldown, error.Category), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = cooldown });
            shared.TrySetException(error); _ = shared.Task.Exception;
        }
        catch (OperationCanceledException) { shared.TrySetCanceled(); }
        catch (Exception error) { shared.TrySetException(error); _ = shared.Task.Exception; }
        finally
        {
            lock (flights)
            {
                if (flights.TryGetValue(key, out var current) && ReferenceEquals(current, flight)) flights.Remove(key);
                flight.Cancellation.Dispose();
            }
        }
    }
    private async Task<object?> Load(string operation, string canonical, string? upstream, CancellationToken cancellation)
    {
        var secret = config["AlphaVantage:ApiKey"] ?? config["ALPHA_VANTAGE_API_KEY"];
        if (string.IsNullOrWhiteSpace(secret)) throw Failure(MarketEnrichmentFailure.ProviderUnavailable);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds), clock);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellation);
        var acquired = false;
        try
        {
            await transport.WaitAsync(timeout.Token);
            acquired = true;
            timeout.Token.ThrowIfCancellationRequested();
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (today != budgetDay) { budgetDay = today; used = 0; }
            if (used >= options.DailyRequestBudget) throw Failure(MarketEnrichmentFailure.LocalBudgetExceeded);
            using var client = factory.CreateClient("AlphaVantage");
            // Never log this URI, request, response body or an upstream exception.
            var path = "query?function=" + operation + (upstream is null ? "" :
                operation == "NEWS_SENTIMENT" ? "&tickers=" + Uri.EscapeDataString(upstream) + "&limit=20&sort=LATEST" : "&symbol=" + Uri.EscapeDataString(upstream)) +
                (operation == "EARNINGS_CALENDAR" ? "&horizon=12month" : "") + "&apikey=" + Uri.EscapeDataString(secret);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            timeout.Token.ThrowIfCancellationRequested();
            used++; // Failed sent attempts consume the local budget too. No retry or cross-provider fallback.
            logger?.LogInformation("AlphaVantage operation {Operation}; local attempt {Attempt}", operation, used);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw Failure(MarketEnrichmentFailure.RateLimited);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw Failure(MarketEnrichmentFailure.PermissionOrEntitlement);
            if (!response.IsSuccessStatusCode) throw Failure(MarketEnrichmentFailure.ProviderUnavailable);
            const int maxBytes = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maxBytes) throw Failure(MarketEnrichmentFailure.MalformedResponse);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bytes.Length + read > maxBytes) throw Failure(MarketEnrichmentFailure.MalformedResponse);
                bytes.Write(buffer, 0, read);
            }
            timeout.Token.ThrowIfCancellationRequested();
            var text = System.Text.Encoding.UTF8.GetString(bytes.ToArray());
            if (text.Contains(secret, StringComparison.Ordinal)) throw Failure(MarketEnrichmentFailure.MalformedResponse);
            var media = response.Content.Headers.ContentType?.MediaType;
            if (text.TrimStart().StartsWith('{'))
            {
                if (media != "application/json") throw Failure(MarketEnrichmentFailure.MalformedResponse);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                CheckError(root);
                return operation switch
                {
                    "OVERVIEW" => Fundamentals(root, canonical, upstream!),
                    "COMPANY_LOGO" => Logo(root, canonical),
                    "TOP_GAINERS_LOSERS" => Movers(root),
                    "NEWS_SENTIMENT" => News(root, canonical, upstream!),
                    _ => throw Failure(MarketEnrichmentFailure.MalformedResponse)
                };
            }
            if (operation != "EARNINGS_CALENDAR" || media is not ("text/csv" or "application/csv" or "application/x-download" or "text/plain"))
                throw Failure(MarketEnrichmentFailure.MalformedResponse);
            return Earnings(text, canonical, upstream!, today);
        }
        catch (MarketEnrichmentException) { throw; }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw Failure(MarketEnrichmentFailure.Timeout); }
        catch (HttpRequestException) { throw Failure(MarketEnrichmentFailure.ProviderUnavailable); }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or OverflowException or MalformedLineException or IOException or KeyNotFoundException)
        { throw Failure(MarketEnrichmentFailure.MalformedResponse); }
        finally { if (acquired) transport.Release(); }
    }
    private static MarketEnrichmentException Failure(MarketEnrichmentFailure kind) => new(kind);
    private static string? Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) || text is "None" or "-" or "null" ? null : text;
    }
    private static decimal? Number(JsonElement root, string name) => ParseNumber(Text(root, name));
    private static decimal? ParseNumber(string? value) => string.IsNullOrWhiteSpace(value) || value is "None" or "-" or "null" ? null : decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static long? Count(JsonElement root, string name)
    {
        var value = Text(root, name);
        if (value is null) return null;
        var count = long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (count < 0) throw new FormatException();
        return count;
    }
    private static void CheckError(JsonElement root)
    {
        foreach (var name in new[] { "Information", "Note", "Error Message" })
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            var message = value.GetString() ?? "";
            var category = message.Contains("premium", StringComparison.OrdinalIgnoreCase) || message.Contains("entitlement", StringComparison.OrdinalIgnoreCase)
                ? MarketEnrichmentFailure.PermissionOrEntitlement
                : message.Contains("day", StringComparison.OrdinalIgnoreCase) || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
                ? MarketEnrichmentFailure.QuotaExceeded
                : name == "Note" ? MarketEnrichmentFailure.RateLimited : MarketEnrichmentFailure.ProviderUnavailable;
            throw Failure(category);
        }
    }
    private static StockFundamentals? Fundamentals(JsonElement root, string canonical, string upstream)
    {
        if (!root.EnumerateObject().Any()) return null;
        if (Text(root, "Symbol") != upstream) throw new FormatException();
        return new StockFundamentals
        {
            Symbol = canonical,
            AssetType = Text(root, "AssetType"),
            Name = Text(root, "Name"),
            Description = Text(root, "Description"),
            Exchange = Text(root, "Exchange"),
            Currency = Text(root, "Currency"),
            Country = Text(root, "Country"),
            Sector = Text(root, "Sector"),
            Industry = Text(root, "Industry"),
            MarketCap = Number(root, "MarketCapitalization"),
            PeRatio = Number(root, "PERatio"),
            PegRatio = Number(root, "PEGRatio"),
            BookValue = Number(root, "BookValue"),
            DividendPerShare = Number(root, "DividendPerShare"),
            DividendYield = Number(root, "DividendYield"),
            EpsTtm = Number(root, "EPS"),
            RevenuePerShareTtm = Number(root, "RevenuePerShareTTM"),
            ProfitMargin = Number(root, "ProfitMargin"),
            OperatingMarginTtm = Number(root, "OperatingMarginTTM"),
            ReturnOnAssetsTtm = Number(root, "ReturnOnAssetsTTM"),
            ReturnOnEquityTtm = Number(root, "ReturnOnEquityTTM"),
            RevenueTtm = Number(root, "RevenueTTM"),
            GrossProfitTtm = Number(root, "GrossProfitTTM"),
            Ebitda = Number(root, "EBITDA"),
            DilutedEpsTtm = Number(root, "DilutedEPSTTM"),
            QuarterlyEarningsGrowthYoy = Number(root, "QuarterlyEarningsGrowthYOY"),
            QuarterlyRevenueGrowthYoy = Number(root, "QuarterlyRevenueGrowthYOY"),
            Beta = Number(root, "Beta"),
            FiftyTwoWeekHigh = Number(root, "52WeekHigh"),
            FiftyTwoWeekLow = Number(root, "52WeekLow"),
            FiftyDayMovingAverage = Number(root, "50DayMovingAverage"),
            TwoHundredDayMovingAverage = Number(root, "200DayMovingAverage"),
            AnalystTargetPrice = Number(root, "AnalystTargetPrice"),
            SharesOutstanding = Count(root, "SharesOutstanding"),
            AnalystRatings = new(Count(root, "AnalystRatingStrongBuy"), Count(root, "AnalystRatingBuy"), Count(root, "AnalystRatingHold"), Count(root, "AnalystRatingSell"), Count(root, "AnalystRatingStrongSell"))
        };
    }
    private static CompanyLogo Logo(JsonElement root, string symbol)
    {
        var reported = Text(root, "symbol");
        if (reported is not null && reported != AlphaSymbol.Resolve(symbol)) throw new FormatException();
        if (root.EnumerateObject().Any() && !root.TryGetProperty("logo_url_png", out _) && !root.TryGetProperty("logo_url_svg", out _) && reported is null) throw new FormatException();
        return new(symbol, LogoUrl(Text(root, "logo_url_png"), ".png"), LogoUrl(Text(root, "logo_url_svg"), ".svg"));
    }
    private static string? LogoUrl(string? value, string extension)
    {
        if (value is null) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "cdn.alphavantage.co" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith("/logos/", StringComparison.Ordinal) ||
            !uri.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Contains('%')) throw new FormatException();
        return uri.AbsoluteUri;
    }
    private static MarketMovers Movers(JsonElement root) => new(Text(root, "last_updated"), Rows(root, "top_gainers"), Rows(root, "top_losers"), Rows(root, "most_actively_traded"));
    private static StockNews News(JsonElement root, string canonical, string ticker)
    {
        var feed = root.GetProperty("feed");
        if (feed.ValueKind != JsonValueKind.Array || feed.GetArrayLength() > 1000) throw new FormatException();
        var articles = new List<StockNewsArticle>();
        foreach (var row in feed.EnumerateArray())
        {
            // The upstream ticker filter includes passing mentions. Require a strong
            // association with this company rather than displaying broad-market news.
            if (!row.TryGetProperty("ticker_sentiment", out var tickers) || tickers.ValueKind != JsonValueKind.Array ||
                !tickers.EnumerateArray().Any(t => Text(t, "ticker") == ticker &&
                    decimal.TryParse(Text(t, "relevance_score"), NumberStyles.Float, CultureInfo.InvariantCulture, out var relevance) &&
                    relevance is >= 0.5m and <= 1m)) continue;
            var title = Text(row, "title"); var source = Text(row, "source"); var link = Text(row, "url");
            if (title is null || title.Length > 500 || source is null || source.Length > 200 || link is null || link.Length > 2048 ||
                !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
                !DateTimeOffset.TryParseExact(Text(row, "time_published"), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var published)) continue;
            articles.Add(new(title, uri.AbsoluteUri, source, published));
        }
        return new(canonical, articles.DistinctBy(a => a.Url).OrderByDescending(a => a.PublishedAtUtc).Take(10).ToArray());
    }
    private static IReadOnlyList<MarketMover> Rows(JsonElement root, string name)
    {
        var rows = root.GetProperty(name);
        if (rows.GetArrayLength() > 100) throw new FormatException();
        return Array.AsReadOnly(rows.EnumerateArray().Select(row =>
        {
            var price = Number(row, "price") ?? throw new FormatException();
            if (price <= 0) throw new FormatException();
            var percent = Text(row, "change_percentage") ?? throw new FormatException();
            if (!percent.EndsWith('%')) throw new FormatException();
            return new MarketMover(Text(row, "ticker") ?? throw new FormatException(), price, Number(row, "change_amount") ?? throw new FormatException(),
                ParseNumber(percent[..^1]) ?? throw new FormatException(), Count(row, "volume") ?? throw new FormatException());
        }).ToArray());
    }
    private static StockEarnings Earnings(string csv, string canonical, string upstream, DateOnly today)
    {
        using var parser = new TextFieldParser(new StringReader(csv)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new FormatException();
        int Column(string name) { var i = Array.IndexOf(headers, name); return i >= 0 ? i : throw new FormatException(); }
        var symbol = Column("symbol"); var report = Column("reportDate"); var fiscal = Column("fiscalDateEnding"); var estimate = Column("estimate"); var currency = Column("currency");
        StockEarnings result = new(canonical, null, null, null, null);
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields()!;
            if (row.Length != headers.Length) throw new FormatException();
            if (row[symbol] != upstream) continue;
            var date = DateOnly.ParseExact(row[report], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (date < today || result.NextEarningsDate is not null && date >= result.NextEarningsDate) continue;
            result = new(canonical, date, string.IsNullOrWhiteSpace(row[fiscal]) ? null : DateOnly.ParseExact(row[fiscal], "yyyy-MM-dd", CultureInfo.InvariantCulture), ParseNumber(row[estimate]), row[currency]);
        }
        return result;
    }
    public void Dispose() { cache.Dispose(); transport.Dispose(); }
}
