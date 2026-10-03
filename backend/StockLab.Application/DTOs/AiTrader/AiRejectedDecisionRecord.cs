namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiRejectedDecisionRecord(
    Guid DecisionId,
    string Symbol,
    AiTradingSignal Signal,
    decimal Confidence,
    DateOnly DecisionDate,
    string ModelName,
    string ModelVersion,
    AiRiskRejectionReason RejectionReason,
    DateTime RejectedAtUtc);
