namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiRejectedDecisionRecordRequest(Guid DecisionId, AiRiskDecision RiskDecision);
