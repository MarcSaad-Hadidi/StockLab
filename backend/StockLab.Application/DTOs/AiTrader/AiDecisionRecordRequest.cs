namespace StockLab.Application.DTOs.AiTrader;

/// <summary>Raw ML output. The caller supplies a stable ID and an explicit, opaque model version.</summary>
public sealed record AiDecisionRecordRequest(
    Guid DecisionId,
    string Symbol,
    AiTradingSignal Signal,
    decimal Confidence,
    DateOnly DecisionDate,
    string ModelName,
    string ModelVersion);
