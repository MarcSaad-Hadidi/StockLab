namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiInferenceSignal(string Symbol, AiTradingSignal Signal, decimal Confidence);

/// <summary>Original inference only; no risk approval or execution state.</summary>
public sealed record AiInferenceBatch(string ModelName, string ModelVersion, DateOnly DecisionDate,
    IReadOnlyList<AiInferenceSignal> Decisions);
