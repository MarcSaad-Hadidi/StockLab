using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StockLab.Api.Middleware;
using StockLab.Application.Exceptions;

namespace StockLab.UnitTests.Api;

public sealed class AiTraderErrorTests
{
    [Theory]
    [InlineData(AiTraderPerformanceFailure.PortfolioNotFound, 404, "ai_trader_not_initialized")]
    [InlineData(AiTraderPerformanceFailure.ConcurrencyConflict, 409, "ai_trader_concurrency_conflict")]
    [InlineData(AiTraderPerformanceFailure.CaptureConflict, 409, "ai_trader_concurrency_conflict")]
    [InlineData(AiTraderPerformanceFailure.PersistenceFailure, 503, "ai_trader_performance_unavailable")]
    [InlineData(AiTraderPerformanceFailure.StorageCapacityExceeded, 503, "ai_trader_performance_unavailable")]
    public async Task Performance_failures_have_controlled_status_and_public_code(
        AiTraderPerformanceFailure failure, int status, string code) =>
        await AssertErrorAsync(new AiTraderPerformanceException(failure), status, code);

    [Theory]
    [InlineData("decision")]
    [InlineData("rejection")]
    [InlineData("trade")]
    [InlineData("database")]
    public async Task History_storage_failures_are_unavailable_without_private_details(string kind)
    {
        Exception failure = kind switch
        {
            "decision" => new AiDecisionHistoryException(AiDecisionHistoryFailure.PersistenceFailure),
            "rejection" => new AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure.PersistenceFailure),
            "trade" => new AiTradesHistoryException(AiTradesHistoryFailure.PersistenceFailure),
            _ => new Microsoft.Data.Sqlite.SqliteException("private-marker sql connection", 1)
        };
        await AssertErrorAsync(failure, 503, "ai_trader_history_unavailable");
    }

    private static async Task AssertErrorAsync(Exception failure, int status, string code)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/ai-trader/performance";
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => Task.FromException(failure),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetString());
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("private-marker", json.RootElement.GetRawText());
    }
}
