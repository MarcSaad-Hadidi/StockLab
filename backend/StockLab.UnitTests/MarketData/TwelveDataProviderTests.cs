using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Exceptions;
using StockLab.Infrastructure.MarketData;

namespace StockLab.UnitTests.MarketData;

public sealed class TwelveDataProviderTests
{
    internal const string QuoteJson = """
        {"symbol":"AAPL","currency":"USD","close":"204.50","change":"1.00","percent_change":"0.4914","volume":"20400000","last_quote_at":1787947200}
        """;
    internal const string SearchJson = """
        {"data":[
          {"symbol":"AAPL","instrument_name":"Apple Inc.","exchange":"NASDAQ","currency":"USD","instrument_type":"Common Stock"},
          {"symbol":"BTC/USD","instrument_name":"Bitcoin","instrument_type":"Digital Currency"},
          {"symbol":"EUR/USD","instrument_name":"Euro","instrument_type":"Physical Currency"},
          {"symbol":"GC","instrument_name":"Gold","instrument_type":"Agricultural Product"},
          {"symbol":"MSFT","instrument_name":"Microsoft","instrument_type":"Common Stock"},
          {"symbol":"AAPL","instrument_name":"Apple Inc.","exchange":"NASDAQ","currency":"USD","instrument_type":"Common Stock"}],"status":"ok"}
        """;
    internal static StockHistoryRequest Calendar(StockHistoryInterval interval = StockHistoryInterval.Day) =>
        new("AAPL", new CalendarHistoryRange(new(2026, 8, 24), new(2026, 8, 29)), interval);
    internal static StockHistoryRequest Intraday(StockHistoryInterval interval = StockHistoryInterval.Minute) =>
        new("AAPL", new IntradayHistoryRange(DateTimeOffset.Parse("2026-08-24T13:30:00Z"), DateTimeOffset.Parse("2026-08-24T15:30:00Z")), interval);
    internal static string HistoryJson(string interval = "1day", string date = "2026-08-24", string extra = "") => $$"""
        {"meta":{"symbol":"AAPL","currency":"USD","interval":"{{interval}}","exchange_timezone":"America/New_York"},
        "values":[{"datetime":"{{date}}","open":"200.1","high":"205.2","low":"199.3","close":"204.4","volume":null}{{extra}}],"status":"ok"}
        """;

    [Fact]
    public async Task Quote_maps_decimal_fields_and_uses_header_only_with_encoded_single_symbol()
    {
        using var f = new Fixture(QuoteJson);
        var result = await f.Provider.GetQuoteAsync(" aapl:NASDAQ ");
        Assert.NotNull(result);
        Assert.Equal("AAPL:NASDAQ", result.Symbol);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(204.5m, result.Price);
        Assert.Equal(1m, result.Change);
        Assert.Equal(0.4914m, result.ChangePercent);
        Assert.Equal(20400000, result.Volume);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1787947200), result.AsOfUtc);
        var request = Assert.Single(f.Handler.Requests);
        Assert.Equal("/quote?symbol=AAPL%3ANASDAQ", request.Uri.PathAndQuery);
        Assert.Equal("apikey WEBSITE-TEST-KEY", request.Authorization);
        Assert.DoesNotContain("TEST-KEY", request.Uri.ToString());
    }

    [Fact]
    public async Task Optional_quote_values_remain_null()
    {
        using var f = new Fixture("""{"symbol":"AAPL","currency":"USD","close":204.5,"last_quote_at":1787947200}""");
        var result = await f.Provider.GetQuoteAsync("AAPL");
        Assert.NotNull(result);
        Assert.Null(result.Change); Assert.Null(result.ChangePercent); Assert.Null(result.Volume);
    }

    [Fact]
    public async Task Search_encodes_query_preserves_relevance_filters_and_deduplicates_canonical_symbols()
    {
        using var f = new Fixture(SearchJson);
        var results = await f.Provider.SearchStocksAsync(" Apple & Co ");
        Assert.Equal(new[] { "AAPL:NASDAQ", "MSFT" }, results.Select(r => r.Symbol));
        Assert.Equal("Apple Inc.", results[0].CompanyName);
        Assert.Equal("NASDAQ", results[0].Exchange); Assert.Equal("USD", results[0].Currency);
        Assert.Null(results[1].Currency);
        Assert.Equal("/symbol_search?symbol=Apple%20%26%20Co", Assert.Single(f.Handler.Requests).Uri.PathAndQuery);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Recognized_unknown_instrument_is_null_or_empty(string operation)
    {
        using var f = new Fixture("""{"status":"error","code":404,"message":"Stock symbol was not found."}""", HttpStatusCode.NotFound);
        var result = await f.Call(operation);
        if (operation == "search") Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<StockSearchResult>>(result));
        else Assert.Null(result);
        Assert.Single(f.Handler.Requests);
    }

    [Fact]
    public async Task Empty_search_is_success()
    {
        using var f = new Fixture("""{"data":[],"status":"ok"}""");
        Assert.Empty(await f.Provider.SearchStocksAsync("unknown"));
    }

    [Theory]
    [InlineData(StockHistoryInterval.Minute, "1min", "2026-08-24 13:30:00")]
    [InlineData(StockHistoryInterval.Hour, "1h", "2026-08-24 13:30:00")]
    [InlineData(StockHistoryInterval.Day, "1day", "2026-08-24")]
    [InlineData(StockHistoryInterval.Week, "1week", "2026-08-24")]
    [InlineData(StockHistoryInterval.Month, "1month", "2026-08-24")]
    public async Task History_maps_every_interval_without_synthesizing_instants(StockHistoryInterval interval, string apiInterval, string date)
    {
        using var f = new Fixture(HistoryJson(apiInterval, date));
        var intraday = interval is StockHistoryInterval.Minute or StockHistoryInterval.Hour;
        var result = await f.Provider.GetHistoryAsync(intraday ? Intraday(interval) : Calendar(interval));
        Assert.NotNull(result);
        var bar = Assert.Single(result.Bars);
        Assert.Equal(200.1m, bar.Open); Assert.Equal(205.2m, bar.High);
        Assert.Equal(199.3m, bar.Low); Assert.Equal(204.4m, bar.Close); Assert.Null(bar.Volume);
        if (intraday)
        {
            Assert.Equal(DateTimeOffset.Parse("2026-08-24T13:30:00Z"), bar.OpenTimeUtc);
            Assert.Null(bar.PeriodDate);
        }
        else
        {
            Assert.Equal(new DateOnly(2026, 8, 24), bar.PeriodDate);
            Assert.Null(bar.OpenTimeUtc);
        }
        var request = Assert.Single(f.Handler.Requests);
        var query = Uri.UnescapeDataString(request.Uri.Query);
        Assert.Equal("/time_series", request.Uri.AbsolutePath);
        Assert.Contains("interval=" + apiInterval, query);
        Assert.Contains("start_date=" + (intraday ? "2026-08-24T13:30:00" : "2026-08-24"), query);
        Assert.Contains("end_date=" + (intraday ? "2026-08-24T15:30:00" : "2026-08-29"), query);
        Assert.Contains("adjust=none", query); Assert.Contains("order=asc", query);
        Assert.Equal(intraday, query.Contains("timezone=UTC"));
        Assert.DoesNotContain("outputsize", query);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_sorts_collapses_duplicates_and_filters_both_bounds(bool intraday)
    {
        var dates = intraday
            ? new[] { "2026-08-24 15:30:00", "2026-08-24 14:30:00", "2026-08-24 13:30:00", "2026-08-24 13:30:00", "2026-08-24 12:30:00" }
            : new[] { "2026-08-29", "2026-08-25", "2026-08-24", "2026-08-24", "2026-08-23" };
        var payload = JsonSerializer.Serialize(new
        {
            meta = new { symbol = "AAPL", currency = "USD", interval = intraday ? "1min" : "1day" },
            values = dates.Select(datetime => new { datetime, open = "1", high = "2", low = "1", close = "2", volume = "4" })
        });
        using var f = new Fixture(payload);
        var result = await f.Provider.GetHistoryAsync(intraday ? Intraday() : Calendar());
        Assert.NotNull(result);
        Assert.Equal(2, result.Bars.Count);
        Assert.All(result.Bars, b => Assert.Equal(4, b.Volume));
        if (intraday) Assert.True(result.Bars[0].OpenTimeUtc < result.Bars[1].OpenTimeUtc);
        else Assert.True(result.Bars[0].PeriodDate < result.Bars[1].PeriodDate);
    }

    [Fact]
    public async Task Empty_history_preserves_known_symbol_metadata()
    {
        using var f = new Fixture("""{"meta":{"symbol":"AAPL","currency":"USD","interval":"1day"},"values":[]}""");
        var result = await f.Provider.GetHistoryAsync(Calendar());
        Assert.NotNull(result); Assert.Empty(result.Bars);
    }

    [Theory]
    [InlineData("quote", "{")]
    [InlineData("search", "{}")]
    [InlineData("search", "{")]
    [InlineData("history", "{")]
    [InlineData("history", "{}")]
    [InlineData("quote", """{"symbol":"AAPL","currency":"USD","close":"bad","last_quote_at":1787947200}""")]
    [InlineData("quote", """{"symbol":"AAPL","currency":"USD","close":"1,234","last_quote_at":1787947200}""")]
    [InlineData("quote", """{"symbol":"AAPL","currency":"USD","close":"1","volume":"-2","last_quote_at":1787947200}""")]
    [InlineData("quote", """{"symbol":"AAPL","currency":"USD","close":"1"}""")]
    public async Task Malformed_data_fails_without_fabricated_values(string operation, string json)
    {
        using var f = new Fixture(json);
        var error = await Assert.ThrowsAsync<MarketDataProviderException>(() => f.Call(operation));
        Assert.Equal(MarketDataProviderFailure.MalformedResponse, error.Category);
        Assert.Single(f.Handler.Requests);
    }

    [Theory]
    [InlineData("bad", "205.2", "199.3")]
    [InlineData("200.1", "100", "199.3")]
    [InlineData("200.1", "205.2", "300")]
    public async Task History_rejects_invalid_ohlc(string open, string high, string low)
    {
        using var f = new Fixture(HistoryJson().Replace("200.1", open).Replace("205.2", high).Replace("199.3", low));
        Assert.Equal(MarketDataProviderFailure.MalformedResponse,
            (await Assert.ThrowsAsync<MarketDataProviderException>(() => f.Call("history"))).Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Excessive_range_is_rejected_before_network(bool intraday)
    {
        using var f = new Fixture(HistoryJson());
        var request = intraday ? Intraday() with
        { Range = new IntradayHistoryRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(5000)) }
            : Calendar() with { Range = new CalendarHistoryRange(new(2000, 1, 1), new(2026, 1, 1)) };
        var error = await Assert.ThrowsAsync<MarketDataProviderException>(() => f.Provider.GetHistoryAsync(request));
        Assert.Equal(MarketDataProviderFailure.RangeTooLarge, error.Category);
        Assert.Empty(f.Handler.Requests);
    }

    [Theory]
    [InlineData(400, MarketDataProviderFailure.InvalidRequest)]
    [InlineData(401, MarketDataProviderFailure.AuthenticationFailed)]
    [InlineData(403, MarketDataProviderFailure.PermissionDenied)]
    [InlineData(404, MarketDataProviderFailure.DataUnavailable)]
    [InlineData(429, MarketDataProviderFailure.UpstreamRateLimited)]
    [InlineData(500, MarketDataProviderFailure.ProviderUnavailable)]
    public async Task Errors_are_safe_and_never_retry_or_fallback(int status, MarketDataProviderFailure category)
    {
        foreach (var operation in new[] { "quote", "search", "history" })
        {
            using var f = new Fixture($$"""{"status":"error","code":{{status}},"message":"WEBSITE-TEST-KEY FALLBACK-TEST-KEY ML-TEST-KEY"}""", (HttpStatusCode)status);
            var error = await Assert.ThrowsAsync<MarketDataProviderException>(() => f.Call(operation));
            Assert.Equal(category, error.Category);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain("TEST-KEY", error.ToString());
            Assert.DoesNotContain("TEST-KEY", string.Join("", f.Logger.Messages));
            Assert.Contains(category.ToString(), string.Join("", f.Logger.Messages));
            Assert.Equal("apikey WEBSITE-TEST-KEY", Assert.Single(f.Handler.Requests).Authorization);
        }
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Cancellation_and_timeout_have_distinct_outcomes_and_one_request(string operation)
    {
        using var f = new Fixture(QuoteJson);
        using var source = new CancellationTokenSource();
        f.Handler.Respond = (_, token) => { source.Cancel(); token.ThrowIfCancellationRequested(); throw new Exception(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Call(operation, source.Token));
        Assert.Single(f.Handler.Requests);
        using var timeout = new Fixture(QuoteJson);
        timeout.Handler.Respond = (_, _) => throw new TaskCanceledException("WEBSITE-TEST-KEY");
        var error = await Assert.ThrowsAsync<MarketDataProviderException>(() => timeout.Call(operation));
        Assert.Equal(MarketDataProviderFailure.Timeout, error.Category);
        Assert.DoesNotContain("TEST-KEY", error.ToString());
        Assert.Single(timeout.Handler.Requests);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("search")]
    [InlineData("history")]
    public async Task Network_failure_is_safe_and_not_retried(string operation)
    {
        using var f = new Fixture(QuoteJson);
        f.Handler.Respond = (_, _) => throw new HttpRequestException("WEBSITE-TEST-KEY");
        Assert.Equal(MarketDataProviderFailure.ProviderUnavailable,
            (await Assert.ThrowsAsync<MarketDataProviderException>(() => f.Call(operation))).Category);
        Assert.Single(f.Handler.Requests);
    }

    [Fact]
    public async Task Invalid_arguments_and_precancellation_send_nothing()
    {
        using var f = new Fixture(QuoteJson);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Provider.GetQuoteAsync("AAPL,MSFT"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Provider.GetQuoteAsync(" "));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Provider.SearchStocksAsync(" "));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Provider.GetHistoryAsync(null!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Provider.GetHistoryAsync(Calendar() with { Interval = StockHistoryInterval.Minute }));
        using var source = new CancellationTokenSource(); source.Cancel();
        foreach (var operation in new[] { "quote", "search", "history" })
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Call(operation, source.Token));
        Assert.Empty(f.Handler.Requests);
    }

    [Fact]
    public async Task Conflicting_duplicates_and_full_response_limit_are_not_silent_successes()
    {
        var duplicate = """,{"datetime":"2026-08-24","open":"200.1","high":"205.2","low":"199.3","close":"203","volume":null}""";
        using var conflicting = new Fixture(HistoryJson(extra: duplicate));
        Assert.Equal(MarketDataProviderFailure.MalformedResponse,
            (await Assert.ThrowsAsync<MarketDataProviderException>(() => conflicting.Call("history"))).Category);
        var payload = JsonSerializer.Serialize(new {
            meta = new { symbol = "AAPL", currency = "USD", interval = "1day" },
            values = Enumerable.Range(0, 5000).Select(_ => new { datetime = "2026-08-24" })
        });
        using var full = new Fixture(payload);
        Assert.Equal(MarketDataProviderFailure.RangeTooLarge,
            (await Assert.ThrowsAsync<MarketDataProviderException>(() => full.Call("history"))).Category);
        Assert.Single(full.Handler.Requests);
    }

    [Fact]
    public async Task Real_httpclient_timeout_cancels_blocked_fake_transport()
    {
        using var f = new Fixture(QuoteJson);
        var cancelled = false;
        f.Handler.Respond = async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cancelled = token.IsCancellationRequested; }
            return Response(QuoteJson);
        };
        var error = await Assert.ThrowsAsync<MarketDataProviderException>(() => f.Call("quote").WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(MarketDataProviderFailure.Timeout, error.Category);
        Assert.True(cancelled);
        Assert.Single(f.Handler.Requests);
    }

    [Theory]
    [InlineData(401, MarketDataProviderFailure.AuthenticationFailed)]
    [InlineData(429, MarketDataProviderFailure.UpstreamRateLimited)]
    [InlineData(500, MarketDataProviderFailure.ProviderUnavailable)]
    public async Task Html_errors_keep_http_category(int status, MarketDataProviderFailure category)
    {
        using var f = new Fixture("<html>WEBSITE-TEST-KEY</html>", (HttpStatusCode)status);
        Assert.Equal(category, (await Assert.ThrowsAsync<MarketDataProviderException>(() => f.Call("quote"))).Category);
        Assert.Single(f.Handler.Requests);
    }

    [Fact]
    public async Task Quote_uses_observation_time_instead_of_bar_open_timestamp()
    {
        using var f = new Fixture(QuoteJson.Replace("{", """{"timestamp":123,"""));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1787947200), (await f.Provider.GetQuoteAsync("AAPL"))!.AsOfUtc);
    }

    [Theory]
    [InlineData("2026-01-12 14:30:00", "2026-01-12T14:00:00Z", "2026-01-12T16:00:00Z")]
    [InlineData("2026-08-24 13:30:00", "2026-08-24T13:00:00Z", "2026-08-24T15:00:00Z")]
    public async Task Explicit_utc_output_does_not_apply_exchange_dst_a_second_time(string date, string from, string to)
    {
        using var f = new Fixture(HistoryJson("1min", date));
        var result = await f.Provider.GetHistoryAsync(new("AAPL",
            new IntradayHistoryRange(DateTimeOffset.Parse(from), DateTimeOffset.Parse(to)), StockHistoryInterval.Minute));
        Assert.Equal(DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal), Assert.Single(result!.Bars).OpenTimeUtc);
    }

    [Fact]
    public void Bar_constructors_enforce_exactly_one_time_kind_and_utc()
    {
        var calendar = new StockHistoryBar(new DateOnly(2026, 8, 24), 1, 2, 1, 2, null);
        Assert.Null(calendar.OpenTimeUtc); Assert.NotNull(calendar.PeriodDate);
        var intraday = new StockHistoryBar(DateTimeOffset.UnixEpoch, 1, 2, 1, 2, null);
        Assert.NotNull(intraday.OpenTimeUtc); Assert.Null(intraday.PeriodDate);
        Assert.Throws<ArgumentException>(() => new StockHistoryBar(DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)), 1, 2, 1, 2, null));
    }

    internal sealed class Fixture : IDisposable
    {
        public readonly Handler Handler;
        public readonly RecordingLog Logger = new();
        public readonly TwelveDataProvider Provider;
        public Fixture(string json, HttpStatusCode status = HttpStatusCode.OK, string active = "Website")
        {
            Handler = new Handler { Respond = (_, _) => Task.FromResult(Response(json, status)) };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TwelveData:Keys:Website"] = "WEBSITE-TEST-KEY", ["TwelveData:Keys:Fallback"] = "FALLBACK-TEST-KEY",
                ["TWELVE_DATA_ML_API_KEY"] = "ML-TEST-KEY"
            }).Build();
            Provider = new(new Factory(Handler), new(config, Options.Create(new TwelveDataOptions { ActiveWebsiteKey = active })), Logger);
        }
        public async Task<object?> Call(string operation, CancellationToken token = default) => operation switch
        {
            "quote" => await Provider.GetQuoteAsync("AAPL", token),
            "search" => await Provider.SearchStocksAsync("apple", token),
            _ => await Provider.GetHistoryAsync(Calendar(), token)
        };
        public void Dispose() => Handler.Dispose();
    }
    internal static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    internal sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://api.twelvedata.com/"), Timeout = TimeSpan.FromSeconds(1) };
    }
    internal sealed class Handler : HttpMessageHandler
    {
        public readonly List<(Uri Uri, string? Authorization)> Requests = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond = (_, _) => throw new InvalidOperationException();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            return Respond(request, cancellationToken);
        }
    }
    internal sealed class RecordingLog : ILogger<TwelveDataProvider>
    {
        public readonly List<string> Messages = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception); Messages.Add(formatter(state, exception));
        }
    }
}
