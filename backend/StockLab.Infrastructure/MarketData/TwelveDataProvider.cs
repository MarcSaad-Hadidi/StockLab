using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.MarketData;

/// <summary>One REST request per operation. No retries, pagination, credential failover or background work.</summary>
public sealed class TwelveDataProvider(
    IHttpClientFactory clients,
    TwelveDataWebsiteCredentialResolver credentials,
    ILogger<TwelveDataProvider> logger) : IMarketDataProvider
{
    public const string ClientName = "TwelveData";

    public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        symbol = NormalizeSymbol(symbol);
        return ExecuteAsync<StockQuote>("Quote", "quote?symbol=" + Escape(symbol), root =>
            new StockQuote(Canonical(root, symbol), Required(root, "currency"),
                PositivePrice(root, "close"), OptionalNumber(root, "change"), OptionalNumber(root, "percent_change"),
                Volume(root), DateTimeOffset.FromUnixTimeSeconds(Integer(root, "last_quote_at")),
                OptionalText(root, "name"), OptionalText(root, "exchange"),
                OptionalPrice(root, "open"), OptionalPrice(root, "high"), OptionalPrice(root, "low"),
                OptionalPrice(root, "previous_close"), Volume(root, "average_volume"),
                OptionalBoolean(root, "is_market_open"), FiftyTwoWeek(root)),
            cancellationToken);
    }

    public async Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return (await ExecuteAsync<IReadOnlyList<StockSearchResult>>("Search", "symbol_search?symbol=" + Escape(query.Trim()),
            root =>
            {
                var results = new List<StockSearchResult>();
                var symbols = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in root.GetProperty("data").EnumerateArray())
                {
                    // Deliberately narrow stock-only allowlist, using the documented instrument type.
                    if (Required(item, "instrument_type") != "Common Stock") continue;
                    var exchange = OptionalText(item, "exchange");
                    var ticker = Required(item, "symbol").Trim().ToUpperInvariant();
                    var canonical = string.IsNullOrWhiteSpace(exchange) ? ticker : ticker + ":" + exchange.Trim().ToUpperInvariant();
                    if (symbols.Add(canonical))
                        results.Add(new StockSearchResult(canonical, Required(item, "instrument_name"), exchange, OptionalText(item, "currency")));
                }
                return results.AsReadOnly();
            }, cancellationToken))!;
    }

    public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var symbol = NormalizeSymbol(request.Symbol);
        var interval = request.Interval switch
        {
            StockHistoryInterval.Minute => "1min", StockHistoryInterval.Hour => "1h",
            StockHistoryInterval.Day => "1day", StockHistoryInterval.Week => "1week", StockHistoryInterval.Month => "1month",
            _ => throw new ArgumentException("Unsupported interval.")
        };
        string from, to;
        long maximum;
        if (request.Range is IntradayHistoryRange r)
        {
            var unit = request.Interval == StockHistoryInterval.Minute ? TimeSpan.TicksPerMinute : TimeSpan.TicksPerHour;
            maximum = (r.ToUtc - r.FromUtc).Ticks / unit + 2; // Include rounded lower and inclusive upper endpoint.
            from = r.FromUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            to = r.ToUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }
        else
        {
            var rDate = (CalendarHistoryRange)request.Range;
            // Conservative even for Week/Month: at most one bar per calendar date, including upper endpoint.
            maximum = (long)rDate.ToDate.DayNumber - rDate.FromDate.DayNumber + 1;
            from = rDate.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            to = rDate.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (maximum > 5000) throw new MarketDataProviderException(MarketDataProviderFailure.RangeTooLarge);
        var uri = $"time_series?symbol={Escape(symbol)}&interval={interval}&start_date={Escape(from)}&end_date={Escape(to)}&adjust=none&order=asc";
        if (request.Range is IntradayHistoryRange) uri += "&timezone=UTC";
        return ExecuteAsync<StockHistory>("History", uri, root =>
        {
            var meta = root.GetProperty("meta");
            if (Required(meta, "interval") != interval) throw new FormatException();
            var values = root.GetProperty("values");
            if (values.GetArrayLength() >= 5000) throw new MarketDataProviderException(MarketDataProviderFailure.RangeTooLarge);
            var bars = new List<StockHistoryBar>();
            foreach (var item in values.EnumerateArray())
            {
                var date = Required(item, "datetime");
                var open = Number(item, "open"); var high = Number(item, "high");
                var low = Number(item, "low"); var close = Number(item, "close"); var volume = Volume(item);
                if (request.Range is IntradayHistoryRange bounds)
                {
                    // timezone=UTC specifies the OUTPUT timezone. exchange_timezone identifies the venue,
                    // not the requested output zone. Do not reinterpret this UTC datetime as local time.
                    var instant = DateTimeOffset.ParseExact(date, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                    var bar = new StockHistoryBar(instant, open, high, low, close, volume);
                    if (instant >= bounds.FromUtc && instant < bounds.ToUtc) bars.Add(bar);
                }
                else
                {
                    var period = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var bar = new StockHistoryBar(period, open, high, low, close, volume);
                    var boundsDate = (CalendarHistoryRange)request.Range;
                    if (period >= boundsDate.FromDate && period < boundsDate.ToDate) bars.Add(bar);
                }
            }
            // Equal duplicates collapse; conflicting duplicates are invalid, never arbitrarily selected.
            var unique = bars.Distinct().ToArray();
            if (unique.GroupBy(b => (b.OpenTimeUtc, b.PeriodDate)).Any(group => group.Count() > 1))
                throw new FormatException();
            return new StockHistory(Canonical(meta, symbol), Required(meta, "currency"), request.Interval,
                Array.AsReadOnly(unique.OrderBy(b => b.OpenTimeUtc).ThenBy(b => b.PeriodDate).ToArray()));
        }, cancellationToken);
    }

    private async Task<T?> ExecuteAsync<T>(string operation, string uri, Func<JsonElement, T> map, CancellationToken token)
        where T : class
    {
        int? status = null;
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("apikey", credentials.Resolve());
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode && status is not (400 or 404))
                throw new MarketDataProviderException(Classify(status.Value));
            // Error bodies are only inspected locally. No raw body or transport exception is retained.
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            // HTTP failures remain classifiable even when a gateway returns HTML instead of JSON.
            JsonDocument parsed;
            try { parsed = JsonDocument.Parse(body); }
            catch (JsonException) when (!response.IsSuccessStatusCode)
            { throw new MarketDataProviderException(Classify(status.Value)); }
            using var json = parsed;
            var root = json.RootElement;
            var errorBody = root.TryGetProperty("status", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "error";
            if (!response.IsSuccessStatusCode || errorBody)
            {
                var code = response.IsSuccessStatusCode && errorBody && root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? n : status.Value;
                // A bare HTTP 404 or ambiguous no-data response does not establish an unknown instrument.
                var message = errorBody ? OptionalText(root, "message") : null;
                if (code is 400 or 404 && message is not null
                    && (message.Contains("symbol", StringComparison.OrdinalIgnoreCase) || message.Contains("instrument", StringComparison.OrdinalIgnoreCase))
                    && (message.Contains("not found", StringComparison.OrdinalIgnoreCase) || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
                {
                    if (operation == "Search") return (T)(object)Array.Empty<StockSearchResult>();
                    return null;
                }
                throw new MarketDataProviderException(Classify(code));
            }
            token.ThrowIfCancellationRequested();
            return map(root);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw Failure(MarketDataProviderFailure.Timeout); }
        catch (HttpRequestException) { throw Failure(MarketDataProviderFailure.ProviderUnavailable); }
        catch (MarketDataProviderException e) { throw Failure(e.Category); }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        { throw Failure(MarketDataProviderFailure.MalformedResponse); }

        MarketDataProviderException Failure(MarketDataProviderFailure category)
        {
            logger.LogWarning("TwelveData operation {Operation} failed: {Category}; HTTP status {Status}.", operation, category, status);
            return new MarketDataProviderException(category);
        }
    }

    private static MarketDataProviderFailure Classify(int code) => code switch
    {
        400 => MarketDataProviderFailure.InvalidRequest, 401 => MarketDataProviderFailure.AuthenticationFailed,
        403 => MarketDataProviderFailure.PermissionDenied, 404 => MarketDataProviderFailure.DataUnavailable,
        429 => MarketDataProviderFailure.UpstreamRateLimited, _ => MarketDataProviderFailure.ProviderUnavailable
    };
    private static decimal PositivePrice(JsonElement root, string name)
    {
        var value = Number(root, name);
        return value > 0 ? value : throw new FormatException();
    }

    private static decimal? OptionalPrice(JsonElement root, string name)
    {
        var value = OptionalNumber(root, name);
        return value is null or > 0 ? value : throw new FormatException();
    }
    private static bool? OptionalBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.GetBoolean();
    }
    private static StockFiftyTwoWeek? FiftyTwoWeek(JsonElement root)
    {
        if (!root.TryGetProperty("fifty_two_week", out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var low = OptionalPrice(value, "low");
        var high = OptionalPrice(value, "high");
        if (low > high) throw new FormatException();
        return new StockFiftyTwoWeek(low, high, OptionalText(value, "range"));
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        // Commas activate Twelve Data batch mode and would spend multiple credits for one permit.
        if (symbol.Contains(',')) throw new ArgumentException("A single symbol is required.", nameof(symbol));
        return symbol.Trim().ToUpperInvariant();
    }
    private static string Canonical(JsonElement root, string requested)
    {
        var returned = Required(root, "symbol").Trim().ToUpperInvariant();
        var separator = requested.IndexOf(':');
        if (returned != (separator < 0 ? requested : requested[..separator])) throw new FormatException();
        return separator < 0 ? returned : returned + requested[separator..];
    }
    private static string Required(JsonElement root, string name) =>
        OptionalText(root, name) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value : throw new FormatException();
    private static string? OptionalText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind != JsonValueKind.String) throw new FormatException();
        return field.GetString();
    }
    private static string? NumericText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null;
        return field.ValueKind switch { JsonValueKind.String => field.GetString(), JsonValueKind.Number => field.GetRawText(), _ => throw new FormatException() };
    }
    private static decimal Number(JsonElement root, string name) => OptionalNumber(root, name) ?? throw new FormatException();
    private static decimal? OptionalNumber(JsonElement root, string name) => NumericText(root, name) is { } text
        ? decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture) : null;
    private static long Integer(JsonElement root, string name) =>
        long.Parse(NumericText(root, name) ?? throw new FormatException(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static long? Volume(JsonElement root, string name = "volume")
    {
        if (NumericText(root, name) is null) return null;
        var result = Integer(root, name);
        return result >= 0 ? result : throw new FormatException();
    }
}
