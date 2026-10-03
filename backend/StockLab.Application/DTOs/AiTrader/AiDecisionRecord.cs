namespace StockLab.Application.DTOs.AiTrader;

/// <summary>Immutable ML history, independent of risk approval and trade execution.</summary>
public sealed record AiDecisionRecord(
    Guid Id,
    string Symbol,
    AiTradingSignal Signal,
    decimal Confidence,
    DateOnly DecisionDate,
    string ModelName,
    string ModelVersion,
    DateTime RecordedAtUtc);
