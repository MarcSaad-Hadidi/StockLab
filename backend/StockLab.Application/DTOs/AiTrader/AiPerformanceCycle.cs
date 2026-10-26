namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiPerformanceCycleObservation(Guid CaptureId, DateTime CycleSlotUtc,
    AiTraderEquityObservation Valuation);
public sealed record AiPerformanceCycleResult(AiPerformanceCycleObservation Cycle, bool IsReplay);
public sealed record AiPerformanceCycleDetail(AiPerformanceCycleObservation Cycle, int TotalTradeCount,
    int Offset, IReadOnlyList<AiPerformanceTradeContext> Trades);
public sealed record AiPerformanceTradeContext(Guid TradeId, Guid? DecisionId, string? ModelName,
    string? ModelVersion, string Symbol, string Side, decimal Quantity, decimal ExecutionPrice,
    decimal? RealizedPnL, DateTime ExecutedAtUtc);
