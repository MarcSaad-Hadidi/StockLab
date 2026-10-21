using System.Data.Common;
using StockLab.Api.DTOs;
using StockLab.Application.Exceptions;

namespace StockLab.Api.Middleware;

internal static class AiTraderErrors
{
    internal static (int Status, ApiErrorResponse Body)? From(Exception exception, HttpContext context) => exception switch
    {
        AiTraderPerformanceException { Category: AiTraderPerformanceFailure.PortfolioNotFound } =>
            (404, new("ai_trader_not_initialized", "The AI Trader portfolio has not been initialized.")),
        AiTraderPerformanceException { Category: AiTraderPerformanceFailure.ConcurrencyConflict or AiTraderPerformanceFailure.CaptureConflict } =>
            (409, new("ai_trader_concurrency_conflict", "AI Trader state changed. Retry the request.")),
        AiTraderPerformanceException => (503, new("ai_trader_performance_unavailable", "AI Trader performance is unavailable.")),
        AiDecisionHistoryException or AiRejectedDecisionHistoryException or AiTradesHistoryException =>
            (503, new("ai_trader_history_unavailable", "AI Trader history is unavailable.")),
        CatalogUnavailableException catalog => (catalog.IsInvalid ? 502 : 503,
            new(catalog.CatalogName + "_catalog_" + (catalog.IsInvalid ? "invalid" : "unavailable"),
                "The requested ML metadata source is " + (catalog.IsInvalid ? "invalid." : "unavailable."))),
        DbException when context.Request.Path.StartsWithSegments("/api/ai-trader") =>
            (503, new("ai_trader_history_unavailable", "AI Trader storage is unavailable.")),
        InvalidOperationException when context.Request.Path.StartsWithSegments("/api/ai-trader") =>
            (503, new("ai_trader_performance_unavailable", "AI Trader data could not be read or valued.")),
        _ => null
    };
}
