using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static StockLab.UnitTests.MarketData.TwelveDataProviderTests;
namespace StockLab.UnitTests.MarketData;

public sealed class EnrichmentApiTests
{
    [Fact]
    public async Task Http_endpoints_and_openapi_are_safe_and_twelve_keeps_its_responsibility()
    {
        using var alpha=new Handler{Respond=(_,_)=>Task.FromResult(Response("{\"Information\":\"quota per day\"}"))};
        using var twelve=new Handler{Respond=(_,_)=>Task.FromResult(Response(QuoteJson))};
        var log=new Logs();
        using var app=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
                ["MarketData:Provider"]="TwelveData",["TwelveData:Keys:Website"]="WEBSITE-TEST-KEY",["TwelveData:ActiveWebsiteKey"]="Website",
                ["AlphaVantage:ApiKey"]="ALPHA-TEST-KEY"}));
            b.ConfigureServices(s=>{
                s.AddHttpClient("AlphaVantage").ConfigurePrimaryHttpMessageHandler(()=>alpha);
                s.AddHttpClient("TwelveData").ConfigurePrimaryHttpMessageHandler(()=>twelve);
                s.AddLogging(l=>l.AddProvider(log));
            });
        });
        using var client=app.CreateClient(new(){BaseAddress=new("https://localhost")});
        var openapi=await client.GetStringAsync("/openapi/v1.json");
        foreach(var route in new[]{"/api/stocks/{symbol}/fundamentals","/api/stocks/{symbol}/logo","/api/stocks/{symbol}/earnings","/api/market/movers"})Assert.Contains(route,openapi);
        foreach(var route in new[]{"/api/stocks/MSFT/fundamentals","/api/stocks/MSFT/logo","/api/stocks/MSFT/earnings","/api/market/movers"})
        {
            var result=await client.GetAsync(route);Assert.Equal(HttpStatusCode.ServiceUnavailable,result.StatusCode);
            Assert.DoesNotContain("ALPHA-TEST-KEY",await result.Content.ReadAsStringAsync());
        }
        Assert.Equal(4,alpha.Requests.Count);
        var quote=await client.GetAsync("/api/stocks/AAPL/quote");Assert.Equal(HttpStatusCode.OK,quote.StatusCode);Assert.Single(twelve.Requests);
        Assert.DoesNotContain("ALPHA-TEST-KEY",openapi);Assert.DoesNotContain("ALPHA-TEST-KEY",string.Join("\n",log.Messages));
        Assert.DoesNotContain("apikey=",string.Join("\n",log.Messages));
    }
    [Fact]
    public async Task Enriched_quote_uses_one_upstream_request_and_preserves_nulls()
    {
        var json=QuoteJson.Replace("{", """{"name":"Apple","exchange":"NASDAQ","open":"200","high":"205","low":"199","previous_close":"203.5","average_volume":"22000000","is_market_open":false,"fifty_two_week":{"low":"150","high":"250","range":"150 - 250"},""");
        using var f=new Fixture(json);var q=await f.Provider.GetQuoteAsync("AAPL");
        Assert.Equal("Apple",q!.Name);Assert.Equal(200m,q.Open);Assert.Equal(22000000,q.AverageVolume);Assert.False(q.IsMarketOpen);
        Assert.Equal(150,q.FiftyTwoWeek!.Low);Assert.Single(f.Handler.Requests);
        using var partial=new Fixture(QuoteJson);var p=await partial.Provider.GetQuoteAsync("AAPL");Assert.Null(p!.Open);Assert.Null(p.FiftyTwoWeek);
    }
    private sealed class Logs:ILoggerProvider,ILogger
    {
        public readonly List<string> Messages=[];public ILogger CreateLogger(string categoryName)=>this;public void Dispose(){}
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? e,Func<TState,Exception?,string> format){lock(Messages)Messages.Add(format(state,e));}
    }
}
