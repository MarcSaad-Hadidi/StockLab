namespace StockLab.Application.DTOs.AiTrader;

/// <summary>Persisted attempt outcome or an authoritative existing decision outcome; never a broker order.</summary>
public sealed record AiRiskExecutionResult(Guid DecisionId, Guid? AttemptId, string Status,
    Guid? TradeId, string? FailureCode, bool IsReplay);
