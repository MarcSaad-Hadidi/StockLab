using System.Net;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;

namespace StockLab.UnitTests.Api;

public sealed class AiPerformanceCycleApiTests
{
    private const string Root = "/api/ai-trader/performance/cycles";

    [Fact]
    public async Task Cycles_require_authentication_and_reads_never_capture_or_quote()
    {
        await using var f = await AiTraderApiFixture.CreateAsync(new() { ["AiPerformanceCycle:ExpectedDatabase"] = "main" });
        using var anonymous = await f.GetAsync(Root);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await f.InitializeAsync();
        await f.ExecuteAsync();
        Guid id;
        using (var scope = f.CreateScope())
            id = (await scope.ServiceProvider.GetRequiredService<IAiPerformanceCycleService>().RunOnceAsync()).Cycle.CaptureId;
        var before = await f.Database.StateAsync();
        f.Market.Quote = (_, _) => throw new InvalidOperationException("must not quote for cycle reads");
        var token = AiTraderApiFixture.Token();
        var list = await f.ReadAsync(Root, token);
        Assert.Equal(id, Assert.Single(list.EnumerateArray()).GetProperty("captureId").GetGuid());
        var detail = await f.ReadAsync($"{Root}/{id}?limit=1", token);
        Assert.Equal(1, detail.GetProperty("totalTradeCount").GetInt32());
        var trade = Assert.Single(detail.GetProperty("trades").EnumerateArray());
        Assert.Equal("test-original-v1", trade.GetProperty("modelVersion").GetString());
        Assert.Equal("10", trade.GetProperty("quantity").GetString());
        Assert.Equal(before, await f.Database.StateAsync());
        using var missing = await f.GetAsync($"{Root}/{Guid.NewGuid()}", token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var path in new[] { "?limit=201", $"/{id}?offset=-1", "/00000000-0000-0000-0000-000000000000" })
        {
            using var invalid = await f.GetAsync(Root + path, token);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }
}
