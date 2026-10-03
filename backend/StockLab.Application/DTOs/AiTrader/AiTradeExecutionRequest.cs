namespace StockLab.Application.DTOs.AiTrader;

/// <summary>Caller-generated OrderId identifies one approved order across retries.</summary>
public sealed record AiTradeExecutionRequest(Guid OrderId, AiRiskDecision RiskDecision);
