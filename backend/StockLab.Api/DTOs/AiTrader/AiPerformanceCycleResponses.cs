using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Api.DTOs.AiTrader;

public sealed record AiPerformanceCycleResponse(Guid CaptureId, DateTime CycleSlotUtc, AiTraderEquityResponse Valuation)
{
    internal static AiPerformanceCycleResponse From(AiPerformanceCycleObservation value) =>
        new(value.CaptureId, AiTraderValues.Utc(value.CycleSlotUtc), AiTraderEquityResponse.From(value.Valuation));
}

public sealed record AiPerformanceCycleDetailResponse(AiPerformanceCycleResponse Cycle, int TotalTradeCount, int Offset,
    IReadOnlyList<AiPerformanceTradeContextResponse> Trades)
{
    internal static AiPerformanceCycleDetailResponse From(AiPerformanceCycleDetail value) =>
        new(AiPerformanceCycleResponse.From(value.Cycle), value.TotalTradeCount, value.Offset,
            value.Trades.Select(AiPerformanceTradeContextResponse.From).ToArray());
}

public sealed record AiPerformanceTradeContextResponse(Guid TradeId, Guid? DecisionId, string? ModelName,
    string? ModelVersion, string Symbol, string Side, string Quantity, string ExecutionPrice, string? RealizedPnL,
    DateTime ExecutedAtUtc)
{
    internal static AiPerformanceTradeContextResponse From(AiPerformanceTradeContext value) =>
        new(value.TradeId, value.DecisionId, value.ModelName, value.ModelVersion, value.Symbol, value.Side,
            AiTraderValues.Amount(value.Quantity)!, AiTraderValues.Amount(value.ExecutionPrice)!,
            AiTraderValues.Amount(value.RealizedPnL), AiTraderValues.Utc(value.ExecutedAtUtc));
}
