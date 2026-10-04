namespace StockLab.Application.DTOs.AiTrader;

/// <summary>Persisted execution facts, including the original ML decision when available.</summary>
public sealed record AiTradeHistoryItem(
    Guid TradeId,
    Guid OrderId,
    Guid? DecisionId,
    string Side,
    string Symbol,
    decimal Quantity,
    decimal ExecutionPrice,
    decimal TotalAmount,
    DateTime ExecutedAtUtc,
    AiTradeDecisionSummary? Decision);
