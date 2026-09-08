using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StockLab.Application.Exceptions;
using StockLab.Infrastructure.MarketEnrichment;
using Xunit;
namespace StockLab.UnitTests.MarketData;

public sealed class AlphaVantageTests
{
    private const string Overview = """{"Symbol":"MSFT","Name":"Microsoft","Exchange":"NASDAQ","Currency":"USD","MarketCapitalization":"1234567890123","PERatio":"21.2","EPS":"9.25","DividendYield":"0.012","Beta":"0.95","AnalystRatingStrongBuy":"12","AnalystTargetPrice":"500"}""";
    [Fact]
    public async Task Maps_finance_and_optional_fields_without_fabricating_zero()
    {
        using var f = new Fixture(Overview);
        var result = await f.Provider.GetFundamentalsAsync(" msft:NASDAQ ");
        Assert.Equal("MSFT:NASDAQ", result!.Symbol); Assert.Equal("Microsoft", result.Name);
        Assert.Equal(1234567890123m, result.MarketCap); Assert.Equal(21.2m, result.PeRatio);
        Assert.Equal(9.25m, result.EpsTtm); Assert.Equal(.012m, result.DividendYield); Assert.Equal(.95m, result.Beta);
        Assert.Equal(12, result.AnalystRatings!.StrongBuy); Assert.Null(result.AnalystRatings.Hold);
        Assert.Null(result.Description); Assert.Equal(500, result.AnalystTargetPrice);
        var uri = Assert.Single(f.Handler.Requests);
        Assert.Contains("function=OVERVIEW", uri); Assert.Contains("symbol=MSFT&", uri); Assert.Contains("apikey=ALPHA-TEST-KEY", uri);
    }
    [Theory]
    [InlineData("None")][InlineData("")][InlineData("-")][InlineData("null")]
    public async Task Missing_numeric_markers_are_null(string marker)
    {
        using var f = new Fixture(Overview.Replace("21.2", marker));
        Assert.Null((await f.Provider.GetFundamentalsAsync("MSFT"))!.PeRatio);
    }
    [Theory]
    [InlineData("{oops")][InlineData("[]")][InlineData("{\"Symbol\":\"MSFT\",\"PERatio\":\"invalid\"}")]
    [InlineData("{\"Symbol\":\"MSFT\",\"Description\":\"ALPHA-TEST-KEY\"}")]
    public async Task Invalid_payloads_are_safe_and_never_retried(string json)
    {
        using var f = new Fixture(json);
        var error = await Assert.ThrowsAsync<MarketEnrichmentException>(() => f.Provider.GetFundamentalsAsync("MSFT"));
        Assert.Equal(MarketEnrichmentFailure.MalformedResponse, error.Category);
        Assert.DoesNotContain("ALPHA-TEST-KEY", error.ToString()); Assert.Null(error.InnerException);
        Assert.Single(f.Handler.Requests);
    }
    [Theory]
    [InlineData("Information", "25 requests per day", MarketEnrichmentFailure.QuotaExceeded)]
    [InlineData("Information", "premium required", MarketEnrichmentFailure.PermissionOrEntitlement)]
    [InlineData("Note", "too many calls", MarketEnrichmentFailure.RateLimited)]
    [InlineData("Error Message", "invalid request", MarketEnrichmentFailure.ProviderUnavailable)]
    public async Task Http_200_error_envelopes_are_failures(string field, string message, MarketEnrichmentFailure category)
    {
        using var f = new Fixture(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string,string> { [field] = message }));
        Assert.Equal(category, (await Assert.ThrowsAsync<MarketEnrichmentException>(() => f.Provider.GetFundamentalsAsync("MSFT"))).Category);
        Assert.Single(f.Handler.Requests);
    }
    [Fact]
    public async Task Twenty_attempts_then_local_rejection_and_utc_reset()
    {
        using var f = new Fixture("{\"Note\":\"limited\"}");
        for (var i=0; i<20; i++) await Assert.ThrowsAsync<MarketEnrichmentException>(() => f.Provider.GetFundamentalsAsync("S"+i));
        Assert.Equal(MarketEnrichmentFailure.LocalBudgetExceeded, (await Assert.ThrowsAsync<MarketEnrichmentException>(() => f.Provider.GetFundamentalsAsync("MSFT"))).Category);
        Assert.Equal(20, f.Handler.Requests.Count);
        f.Clock.Now = f.Clock.Now.AddDays(1);
        await Assert.ThrowsAsync<MarketEnrichmentException>(() => f.Provider.GetFundamentalsAsync("MSFT"));
        Assert.Equal(21, f.Handler.Requests.Count);
    }
    [Fact]
    public async Task Twenty_joiners_and_cache_hits_cost_one_call_and_cancellation_is_isolated()
    {
        using var f = new Fixture(Overview);
        var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        f.Handler.Respond = async _ => { entered.SetResult(); await release.Task; return Response(Overview); };
        using var cancel = new CancellationTokenSource();
        var abandoned = f.Provider.GetFundamentalsAsync("MSFT", cancel.Token);
        await entered.Task;
        var others = Enumerable.Range(0, 20).Select(_ => f.Provider.GetFundamentalsAsync("MSFT")).ToArray();
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        release.SetResult(); await Task.WhenAll(others);
        await f.Provider.GetFundamentalsAsync("MSFT"); Assert.Single(f.Handler.Requests);
    }
    [Fact]
    public async Task Cache_expiration_and_distinct_calls_are_globally_serialized()
    {
        using var f = new Fixture("{}");
        var active=0; var maximum=0;
        f.Handler.Respond = async _ => { var n=Interlocked.Increment(ref active); maximum=Math.Max(maximum,n); await Task.Delay(10); Interlocked.Decrement(ref active); return Response("{}"); };
        await Task.WhenAll(Enumerable.Range(0, 5).Select(i=>f.Provider.GetFundamentalsAsync("S"+i)));
        Assert.Equal(1, maximum); Assert.Equal(5, f.Handler.Requests.Count);
        await f.Provider.GetFundamentalsAsync("S0"); Assert.Equal(5, f.Handler.Requests.Count);
        f.Clock.Now=f.Clock.Now.AddHours(25);
        await f.Provider.GetFundamentalsAsync("S0"); Assert.Equal(6, f.Handler.Requests.Count);
    }
    [Fact]
    public async Task Logo_maps_only_safe_public_png_svg_urls_and_caches_thirty_days()
    {
        using var f = new Fixture("""{"symbol":"MSFT","logo_url_png":"https://cdn.alphavantage.co/logos/MSFT.png","logo_url_svg":"https://cdn.alphavantage.co/logos/MSFT.svg"}""");
        var logo = await f.Provider.GetLogoAsync("MSFT"); Assert.EndsWith(".png", logo.PngUrl); Assert.EndsWith(".svg", logo.SvgUrl);
        f.Clock.Now=f.Clock.Now.AddDays(29); await f.Provider.GetLogoAsync("MSFT"); Assert.Single(f.Handler.Requests);
        f.Clock.Now=f.Clock.Now.AddDays(2); await f.Provider.GetLogoAsync("MSFT"); Assert.Equal(2,f.Handler.Requests.Count);
    }
    [Theory]
    [InlineData("https://evil.example/MSFT.png")][InlineData("https://cdn.alphavantage.co/logos/MSFT.png?apikey=fake")]
    [InlineData("http://cdn.alphavantage.co/logos/MSFT.png")][InlineData("https://cdn.alphavantage.co/logos/MSFT.html")]
    public async Task Unsafe_logo_urls_rejected(string url)
    {
        using var f = new Fixture(System.Text.Json.JsonSerializer.Serialize(new { logo_url_png=url }));
        await Assert.ThrowsAsync<MarketEnrichmentException>(()=>f.Provider.GetLogoAsync("MSFT"));
    }
    [Fact]
    public async Task Earnings_csv_selects_next_date_and_handles_quoted_commas()
    {
        using var f = new Fixture("symbol,name,reportDate,fiscalDateEnding,estimate,currency\nMSFT,\"Microsoft, Inc\",2026-09-06,2026-06-30,1.2,USD\nMSFT,Microsoft,2026-10-01,2026-09-30,2.5,USD\nMSFT,Microsoft,2026-09-08,2026-06-30,None,USD", "text/csv");
        var earnings=await f.Provider.GetEarningsAsync("MSFT:NASDAQ");
        Assert.Equal(new DateOnly(2026,9,8),earnings.NextEarningsDate); Assert.Null(earnings.Estimate);
        await f.Provider.GetEarningsAsync("MSFT:NASDAQ"); Assert.Single(f.Handler.Requests);
        Assert.Contains("horizon=12month",f.Handler.Requests[0]);
    }
    [Fact]
    public async Task Empty_calendar_is_unavailable_not_fake_date()
    {
        using var f = new Fixture("symbol,name,reportDate,fiscalDateEnding,estimate,currency\n", "text/csv");
        Assert.Null((await f.Provider.GetEarningsAsync("MSFT")).NextEarningsDate);
    }
    [Fact]
    public async Task Movers_one_eod_call_populates_three_lists_and_is_cached()
    {
        const string row="""[{"ticker":"MSFT","price":"400.5","change_amount":"2.5","change_percentage":"0.6%","volume":"25000"}]""";
        using var f = new Fixture("{\"last_updated\":\"2026-09-04\",\"top_gainers\":"+row+",\"top_losers\":"+row+",\"most_actively_traded\":"+row+"}");
        var movers=await f.Provider.GetMoversAsync(); Assert.Equal(400.5m,Assert.Single(movers.Gainers).Price);
        Assert.Equal(.6m,Assert.Single(movers.Losers).ChangePercent); Assert.Single(movers.MostActive);
        await f.Provider.GetMoversAsync(); Assert.Single(f.Handler.Requests); Assert.DoesNotContain("entitlement",f.Handler.Requests[0]);
    }
    [Fact]
    public async Task Network_timeout_and_pre_cancel_never_retry_or_leak_uri()
    {
        using var f = new Fixture(Overview);
        f.Handler.Respond=_=>throw new HttpRequestException("https://provider/query?apikey=ALPHA-TEST-KEY");
        var error=await Assert.ThrowsAsync<MarketEnrichmentException>(()=>f.Provider.GetFundamentalsAsync("MSFT"));
        Assert.DoesNotContain("ALPHA-TEST-KEY",error.ToString()); Assert.Single(f.Handler.Requests);
        f.Handler.Respond=async token=>{await Task.Delay(Timeout.Infinite,token);return Response(Overview);};
        Assert.Equal(MarketEnrichmentFailure.Timeout,(await Assert.ThrowsAsync<MarketEnrichmentException>(()=>f.Provider.GetFundamentalsAsync("AAPL"))).Category);
        using var cancel=new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Provider.GetFundamentalsAsync("MSFT",cancel.Token));
        Assert.Equal(2,f.Handler.Requests.Count);
    }
    [Theory]
    [InlineData("tsla:NASDAQ","TSLA")][InlineData("IBM:NYSE","IBM")][InlineData("SHOP.TRT","SHOP.TRT")]
    public void Symbols_preserve_international_suffixes(string symbol,string expected)=>Assert.Equal(expected,AlphaSymbol.Resolve(symbol));
    [Theory]
    [InlineData("TSLA:LSE")][InlineData("TSLA:UNKNOWN")]
    public void Ambiguous_exchanges_are_not_silently_stripped(string symbol)=>Assert.Throws<NotSupportedException>(()=>AlphaSymbol.Resolve(symbol));
    [Theory]
    [InlineData(302)][InlineData(429)][InlineData(500)]
    public async Task Http_failures_do_not_retry(int status)
    {
        using var f=new Fixture("{}"); f.Handler.Respond=_=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        await Assert.ThrowsAsync<MarketEnrichmentException>(()=>f.Provider.GetLogoAsync("MSFT")); Assert.Single(f.Handler.Requests);
    }
    [Theory]
    [InlineData("text/html", "<html>error</html>")]
    [InlineData("image/png", "not json")]
    [InlineData("application/json", "{invalid")]
    public async Task Logo_metadata_content_type_is_validated(string media,string body)
    {
        using var f=new Fixture(body,media); await Assert.ThrowsAsync<MarketEnrichmentException>(()=>f.Provider.GetLogoAsync("MSFT"));
    }
    [Fact]
    public async Task Response_size_is_bounded_and_missing_logo_has_explicit_fallback()
    {
        using var f=new Fixture(new string('x',2*1024*1024+1));
        await Assert.ThrowsAsync<MarketEnrichmentException>(()=>f.Provider.GetLogoAsync("MSFT"));
        using var missing=new Fixture("{}"); var logo=await missing.Provider.GetLogoAsync("MSFT"); Assert.Null(logo.PngUrl);Assert.Null(logo.SvgUrl);
    }
    [Fact]
    public async Task Malformed_csv_and_movers_do_not_become_empty_successes()
    {
        using var f=new Fixture("bad,headers", "text/csv");await Assert.ThrowsAsync<MarketEnrichmentException>(()=>f.Provider.GetEarningsAsync("MSFT"));
        using var m=new Fixture("{}");await Assert.ThrowsAsync<MarketEnrichmentException>(()=>m.Provider.GetMoversAsync());
    }
    [Fact]
    public void Options_enforce_positive_ttls_and_free_budget_bound()
    {
        Assert.True(new AlphaVantageOptions().IsValid()); Assert.False(new AlphaVantageOptions{DailyRequestBudget=26}.IsValid());
        Assert.False(new AlphaVantageOptions{LogoTtl=TimeSpan.Zero}.IsValid()); Assert.False(new AlphaVantageOptions{TimeoutSeconds=0}.IsValid());
    }
    [Fact]
    public async Task Repeated_logo_outage_uses_cooldown_without_spending_more_budget()
    {
        using var f=new Fixture("{\"Information\":\"premium required\"}");
        for(var i=0;i<10;i++) await Assert.ThrowsAsync<MarketEnrichmentException>(()=>f.Provider.GetLogoAsync("MSFT"));
        Assert.Single(f.Handler.Requests);
    }
    private static HttpResponseMessage Response(string text,string media="application/json")=>new(HttpStatusCode.OK){Content=new StringContent(text,Encoding.UTF8,media)};
    private sealed class Clock:TimeProvider { public DateTimeOffset Now=new(2026,9,7,12,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Handler:HttpMessageHandler
    {
        public List<string> Requests { get; }=[];
        public required Func<CancellationToken,Task<HttpResponseMessage>> Respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests.Add(request.RequestUri!.ToString());return Respond(token);}
    }
    private sealed class Factory(Handler handler):IHttpClientFactory { public HttpClient CreateClient(string name)=>new(handler,false){BaseAddress=new("https://www.alphavantage.co/")}; }
    private sealed class Fixture:IDisposable
    {
        public Handler Handler; public Clock Clock=new(); public AlphaVantageProvider Provider;
        public Fixture(string body,string media="application/json")
        {
            Handler=new(){Respond=_=>Task.FromResult(Response(body,media))};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["AlphaVantage:ApiKey"]="ALPHA-TEST-KEY"}).Build();
            Provider=new(new Factory(Handler),config,Options.Create(new AlphaVantageOptions{TimeoutSeconds=1}),Clock);
        }
        public void Dispose(){Provider.Dispose();Handler.Dispose();}
    }
}
