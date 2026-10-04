namespace StockLab.Application.DTOs.AiTrader;

/// <summary>Stable order ID for retries and required ID of the already recorded raw ML decision.</summary>
public sealed record AiTradeExecutionRequest(Guid OrderId, Guid DecisionId, AiRiskDecision RiskDecision);
