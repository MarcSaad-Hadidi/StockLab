namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiTradeDecisionSummary(
    Guid DecisionId,
    AiTradingSignal Signal,
    decimal Confidence,
    DateOnly DecisionDate,
    string ModelName,
    string ModelVersion);
