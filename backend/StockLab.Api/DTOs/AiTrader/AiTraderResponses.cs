using System.Globalization;
using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Api.DTOs.AiTrader;

public sealed record AiTraderCoverageResponse(int ValuationSampleCount, DateTime? ValuationFirstAtUtc,
    DateTime? ValuationLastAtUtc, string Status)
{
    internal static AiTraderCoverageResponse From(AiValuationHistoricalCoverage value) => new(value.ValuationSampleCount,
        value.ValuationFirstAtUtc is { } first ? AiTraderValues.Utc(first) : null,
        value.ValuationLastAtUtc is { } last ? AiTraderValues.Utc(last) : null, value.Status.ToString());
}

public sealed record AiTraderOverviewResponse(Guid PortfolioId, string Currency, string InitialCapital, string CashBalance,
    string PositionsMarketValue, string TotalValue, string TotalPnL, decimal TotalReturn, decimal? WinRate,
    decimal? MaxDrawdown, int ExecutedTradeCount, int OpenPositionsCount, DateTime ValuationTimestampUtc,
    AiTraderCoverageResponse HistoricalCoverage)
{
    internal static AiTraderOverviewResponse From(AiTraderPerformanceSnapshot value) => new(value.PortfolioId, value.Currency,
        AiTraderValues.Amount(value.InitialCapital)!, AiTraderValues.Amount(value.CashBalance)!,
        AiTraderValues.Amount(value.PositionsMarketValue)!, AiTraderValues.Amount(value.TotalValue)!,
        AiTraderValues.Amount(value.TotalPnL)!, value.TotalReturn, value.WinRate, value.MaxDrawdown,
        value.ExecutedTradeCount, value.OpenPositionsCount, AiTraderValues.Utc(value.ValuationTimestampUtc),
        AiTraderCoverageResponse.From(value.HistoricalCoverage));
}

public sealed record AiTraderPositionResponse(string Symbol, string Quantity, string AveragePrice, string CurrentPrice,
    string MarketValue, string PnL)
{
    internal static AiTraderPositionResponse From(AiCurrentPosition value) => new(value.Symbol,
        AiTraderValues.Amount(value.Quantity)!, AiTraderValues.Amount(value.AveragePrice)!,
        AiTraderValues.Amount(value.CurrentPrice)!, AiTraderValues.Amount(value.MarketValue)!, AiTraderValues.Amount(value.PnL)!);
}

public sealed record AiTraderTradeDecisionResponse(Guid DecisionId, string Signal, decimal Confidence,
    DateOnly DecisionDate, string ModelName, string ModelVersion);

public sealed record AiTraderTradeResponse(Guid TradeId, Guid OrderId, Guid? DecisionId, string Side, string Symbol,
    string Quantity, string ExecutionPrice, string TotalAmount, DateTime ExecutedAtUtc,
    string? RealizedPnL, AiTraderTradeDecisionResponse? Decision)
{
    internal static AiTraderTradeResponse From(AiTradeHistoryItem value) => new(value.TradeId, value.OrderId,
        value.DecisionId, value.Side, value.Symbol, AiTraderValues.Amount(value.Quantity)!,
        AiTraderValues.Amount(value.ExecutionPrice)!, AiTraderValues.Amount(value.TotalAmount)!,
        AiTraderValues.Utc(value.ExecutedAtUtc), AiTraderValues.Amount(value.RealizedPnL),
        value.Decision is { } decision ? new(decision.DecisionId, AiTraderValues.Signal(decision.Signal),
            decision.Confidence, decision.DecisionDate, decision.ModelName, decision.ModelVersion) : null);
}

public sealed record AiTraderDecisionResponse(Guid DecisionId, string Symbol, string Signal, decimal Confidence,
    DateOnly DecisionDate, string ModelName, string ModelVersion, DateTime RecordedAtUtc,
    string ExecutionStatus, string? RejectionReason, Guid? TradeId)
{
    internal static AiTraderDecisionResponse From(AiDecisionRecord value, AiDecisionOutcome outcome) => new(value.Id,
        value.Symbol, AiTraderValues.Signal(value.Signal), value.Confidence, value.DecisionDate, value.ModelName,
        value.ModelVersion, AiTraderValues.Utc(value.RecordedAtUtc), outcome.ExecutionStatus, outcome.RejectionReason, outcome.TradeId);
}

public sealed record AiTraderRejectedDecisionResponse(Guid DecisionId, string Symbol, string Signal, decimal Confidence,
    DateOnly DecisionDate, string ModelName, string ModelVersion, string RejectionReason, DateTime RejectedAtUtc)
{
    internal static AiTraderRejectedDecisionResponse From(AiRejectedDecisionRecord value) => new(value.DecisionId,
        value.Symbol, AiTraderValues.Signal(value.Signal), value.Confidence, value.DecisionDate, value.ModelName,
        value.ModelVersion, value.RejectionReason.ToString(), AiTraderValues.Utc(value.RejectedAtUtc));
}

public sealed record AiTraderRealizedTradeResponse(Guid TradeId, string Symbol, string Quantity,
    string ExecutionPrice, DateTime ExecutedAtUtc, string RealizedPnL)
{
    internal static AiTraderRealizedTradeResponse? From(AiRealizedTrade? value) => value is null ? null : new(value.TradeId,
        value.Symbol, AiTraderValues.Amount(value.Quantity)!, AiTraderValues.Amount(value.ExecutionPrice)!,
        AiTraderValues.Utc(value.ExecutedAtUtc), AiTraderValues.Amount(value.RealizedPnL)!);
}

public sealed record AiTraderPerformanceResponse(Guid PortfolioId, string Currency, string InitialCapital, string CashBalance,
    string PositionsMarketValue, string TotalValue, string TotalPnL, decimal TotalReturn, string? RealizedPnL,
    string UnrealizedPnL, decimal? WinRate, string? AverageWin, string? AverageLoss,
    AiTraderRealizedTradeResponse? BestTrade, AiTraderRealizedTradeResponse? WorstTrade, decimal? MaxDrawdown,
    int ExecutedTradeCount, int BuyTradeCount, int SellTradeCount, int WinningSellCount, int LosingSellCount,
    int BreakEvenSellCount, int OpenPositionsCount, bool TradeStatisticsComplete, int UnvaluedLegacySellCount,
    DateTime ValuationTimestampUtc, AiTraderCoverageResponse HistoricalCoverage)
{
    internal static AiTraderPerformanceResponse From(AiTraderPerformanceSnapshot value) => new(value.PortfolioId,
        value.Currency, AiTraderValues.Amount(value.InitialCapital)!, AiTraderValues.Amount(value.CashBalance)!,
        AiTraderValues.Amount(value.PositionsMarketValue)!, AiTraderValues.Amount(value.TotalValue)!,
        AiTraderValues.Amount(value.TotalPnL)!, value.TotalReturn, AiTraderValues.Amount(value.RealizedPnL),
        AiTraderValues.Amount(value.UnrealizedPnL)!, value.WinRate, AiTraderValues.Amount(value.AverageWin),
        AiTraderValues.Amount(value.AverageLoss), AiTraderRealizedTradeResponse.From(value.BestTrade),
        AiTraderRealizedTradeResponse.From(value.WorstTrade), value.MaxDrawdown, value.ExecutedTradeCount,
        value.BuyTradeCount, value.SellTradeCount, value.WinningSellCount, value.LosingSellCount,
        value.BreakEvenSellCount, value.OpenPositionsCount, value.TradeStatisticsComplete,
        value.UnvaluedLegacySellCount, AiTraderValues.Utc(value.ValuationTimestampUtc),
        AiTraderCoverageResponse.From(value.HistoricalCoverage));
}

public sealed record AiTraderEquityResponse(DateTime ValuedAtUtc, string CashBalance, string PositionsValue,
    string TotalValue, string PnL, decimal Return, string Currency, bool IsInitialBaseline)
{
    internal static AiTraderEquityResponse From(AiTraderEquityObservation value) => new(AiTraderValues.Utc(value.ValuedAtUtc),
        AiTraderValues.Amount(value.CashBalance)!, AiTraderValues.Amount(value.PositionsValue)!,
        AiTraderValues.Amount(value.TotalValue)!, AiTraderValues.Amount(value.PnL)!, value.Return,
        value.Currency, value.IsInitialBaseline);
}

internal static class AiTraderValues
{
    internal static string? Amount(decimal? value) => value?.ToString("0.############################", CultureInfo.InvariantCulture);
    internal static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    internal static string Signal(AiTradingSignal value) => value switch
    {
        AiTradingSignal.Buy => "BUY", AiTradingSignal.Sell => "SELL", AiTradingSignal.Hold => "HOLD",
        _ => throw new InvalidOperationException("Unknown persisted AI signal.")
    };
}
