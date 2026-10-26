using System.Net;
using System.Text.Json;
using static StockLab.UnitTests.Api.AiTraderApiTests;

namespace StockLab.UnitTests.Api;

public sealed class AiTraderApiOpenApiTests
{
    [Fact]
    public async Task OpenAPI_documents_all_GET_routes_bearer_security_errors_and_public_JSON_types()
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        using var response = await f.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var paths = root.GetProperty("paths");
        var expected = new[] { "overview", "positions", "trades", "trades/{tradeId}", "decisions",
            "decisions/{decisionId}", "rejected-decisions", "performance", "performance/history",
            "performance/cycles", "performance/cycles/{captureId}", "models", "backtests" };
        Assert.Equal(expected.Select(p => "/api/ai-trader/" + p).Order(),
            paths.EnumerateObject().Where(p => p.Name.StartsWith("/api/ai-trader", StringComparison.Ordinal)).Select(p => p.Name).Order());
        foreach (var route in expected)
        {
            var path = paths.GetProperty("/api/ai-trader/" + route);
            var operation = path.GetProperty("get");
            Assert.False(path.TryGetProperty("post", out _));
            Assert.False(path.TryGetProperty("put", out _));
            Assert.False(path.TryGetProperty("delete", out _));
            Assert.Contains(operation.GetProperty("security").EnumerateArray(), security => security.TryGetProperty("Bearer", out _));
            foreach (var status in new[] { "200", "400", "401", "500", "503" })
                Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _), $"{route} missing {status}");
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
            if (route is "models" or "backtests")
                Assert.True(operation.GetProperty("responses").TryGetProperty("502", out _));
            if (operation.TryGetProperty("parameters", out var parameters))
                Assert.DoesNotContain(parameters.EnumerateArray(), p => p.GetProperty("name").GetString() is "userId" or "portfolioId" or "accountId");
        }
        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
        var overview = ResponseSchema(root, paths, "overview");
        foreach (var name in new[] { "initialCapital", "cashBalance", "positionsMarketValue", "totalValue", "totalPnL" })
            AssertSchemaType(overview.GetProperty("properties").GetProperty(name), "string");
        foreach (var name in new[] { "totalReturn", "winRate", "maxDrawdown" })
            AssertSchemaType(overview.GetProperty("properties").GetProperty(name), "number");
        var position = Resolve(root, ResponseSchema(root, paths, "positions").GetProperty("items"));
        foreach (var name in new[] { "quantity", "averagePrice", "currentPrice", "marketValue", "pnL" })
            AssertSchemaType(position.GetProperty("properties").GetProperty(name), "string");
        var trade = ResponseSchema(root, paths, "trades/{tradeId}");
        foreach (var name in new[] { "quantity", "executionPrice", "totalAmount", "realizedPnL" })
            AssertSchemaType(trade.GetProperty("properties").GetProperty(name), "string");
        var performance = ResponseSchema(root, paths, "performance");
        foreach (var name in new[] { "realizedPnL", "unrealizedPnL", "averageWin", "averageLoss" })
            AssertSchemaType(performance.GetProperty("properties").GetProperty(name), "string");
        var decision = ResponseSchema(root, paths, "decisions/{decisionId}");
        foreach (var name in new[] { "decisionId", "symbol", "signal", "confidence", "decisionDate", "modelName",
            "modelVersion", "recordedAtUtc", "executionStatus", "rejectionReason", "tradeId" })
            Assert.True(decision.GetProperty("properties").TryGetProperty(name, out _));
        AssertSchemaType(decision.GetProperty("properties").GetProperty("confidence"), "number");
        var equity = Resolve(root, ResponseSchema(root, paths, "performance/history").GetProperty("items"));
        foreach (var name in new[] { "cashBalance", "positionsValue", "totalValue", "pnL" })
            AssertSchemaType(equity.GetProperty("properties").GetProperty(name), "string");
        AssertSchemaType(equity.GetProperty("properties").GetProperty("return"), "number");
        var cycle = ResponseSchema(root, paths, "performance/cycles/{captureId}");
        var context = Resolve(root, cycle.GetProperty("properties").GetProperty("trades").GetProperty("items"));
        foreach (var name in new[] { "quantity", "executionPrice", "realizedPnL" })
            AssertSchemaType(context.GetProperty("properties").GetProperty(name), "string");
        var model = Resolve(root, ResponseSchema(root, paths, "models").GetProperty("items"));
        foreach (var name in new[] { "modelName", "modelVersion", "modelType", "status", "trainedAtUtc" })
            AssertSchemaType(model.GetProperty("properties").GetProperty(name), "string");
        foreach (var name in new[] { "accuracy", "precision", "recall", "f1" })
            AssertSchemaType(model.GetProperty("properties").GetProperty(name), "number");
        var backtest = Resolve(root, ResponseSchema(root, paths, "backtests").GetProperty("items"));
        foreach (var name in new[] { "initialCapital", "finalValue" })
            AssertSchemaType(backtest.GetProperty("properties").GetProperty(name), "string");
        foreach (var name in new[] { "totalReturn", "winRate", "maxDrawdown", "benchmarkReturn", "returnDelta" })
            AssertSchemaType(backtest.GetProperty("properties").GetProperty(name), "number");
        foreach (var route in new[] { "trades", "decisions", "rejected-decisions", "performance/history",
            "performance/cycles", "performance/cycles/{captureId}" })
        {
            var limit = Assert.Single(paths.GetProperty("/api/ai-trader/" + route).GetProperty("get")
                .GetProperty("parameters").EnumerateArray(), p => string.Equals(
                    p.GetProperty("name").GetString(), "limit", StringComparison.OrdinalIgnoreCase));
            var schema = limit.GetProperty("schema");
            Assert.Equal(1, schema.GetProperty("minimum").GetInt32());
            Assert.Equal(200, schema.GetProperty("maximum").GetInt32());
        }
        Assert.Contains("fraction", paths.GetProperty("/api/ai-trader/overview").GetProperty("get")
            .GetProperty("description").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("trades")]
    [InlineData("decisions")]
    [InlineData("performance/history")]
    [InlineData("performance/cycles")]
    [InlineData("performance/cycles/11111111-1111-1111-1111-111111111111")]
    public async Task Read_routes_do_not_accept_writes(string route)
    {
        await using var f = await AiTraderApiFixture.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai-trader/" + route);
        request.Headers.Authorization = new("Bearer", AiTraderApiFixture.Token());
        using var response = await f.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(0, f.Observer.SaveCalls);
    }

    private static JsonElement ResponseSchema(JsonElement root, JsonElement paths, string route) => Resolve(root,
        paths.GetProperty("/api/ai-trader/" + route).GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema"));

    private static JsonElement Resolve(JsonElement root, JsonElement schema) => schema.TryGetProperty("$ref", out var reference)
        ? root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/').Last()) : schema;

    private static void AssertSchemaType(JsonElement schema, string type)
    {
        var actual = schema.GetProperty("type");
        if (actual.ValueKind == JsonValueKind.Array)
            Assert.Contains(actual.EnumerateArray(), entry => entry.GetString() == type);
        else Assert.Equal(type, actual.GetString());
    }
}
