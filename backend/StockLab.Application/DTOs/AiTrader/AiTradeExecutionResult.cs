namespace StockLab.Application.DTOs.AiTrader;

/// <summary>Committed execution and balances immediately after this trade, including on replay.</summary>
public sealed record AiTradeExecutionResult(
    Guid TradeId,
    Guid? DecisionId,
    Guid OrderId,
    Guid AiTraderPortfolioId,
    string Side,
    string Symbol,
    decimal RiskApprovedQuantity,
    decimal ExecutedQuantity,
    decimal RiskPrice,
    decimal ExecutionPrice,
    decimal TotalAmount,
    decimal CashBalance,
    decimal PositionQuantity,
    decimal? AverageCost,
    DateTime ExecutedAtUtc,
    bool IsIdempotentReplay);
