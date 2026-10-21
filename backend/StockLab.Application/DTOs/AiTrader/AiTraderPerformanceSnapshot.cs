namespace StockLab.Application.DTOs.AiTrader;

/// <summary>USD amounts are decimal; return, win rate and observed drawdown are fractions.</summary>
public sealed record AiTraderPerformanceSnapshot(
    Guid PortfolioId, string Currency, decimal InitialCapital, decimal CashBalance,
    decimal PositionsMarketValue, decimal TotalValue, decimal TotalPnL, decimal TotalReturn,
    decimal? RealizedPnL, decimal UnrealizedPnL, decimal? WinRate, decimal? AverageWin,
    decimal? AverageLoss, AiRealizedTrade? BestTrade, AiRealizedTrade? WorstTrade, decimal? MaxDrawdown,
    int ExecutedTradeCount, int BuyTradeCount, int SellTradeCount, int WinningSellCount,
    int LosingSellCount, int BreakEvenSellCount, int OpenPositionsCount,
    bool TradeStatisticsComplete, int UnvaluedLegacySellCount, DateTime ValuationTimestampUtc,
    AiValuationHistoricalCoverage HistoricalCoverage);

public sealed record AiRealizedTrade(Guid TradeId, string Symbol, decimal Quantity,
    decimal ExecutionPrice, DateTime ExecutedAtUtc, decimal RealizedPnL);

public enum AiValuationCoverageStatus { Absent, Insufficient, SampledSinceInitialization, PartialHistory }

/// <summary>Even a baseline does not imply continuous or exact intraday coverage.</summary>
public sealed record AiValuationHistoricalCoverage(int ValuationSampleCount,
    DateTime? ValuationFirstAtUtc, DateTime? ValuationLastAtUtc, AiValuationCoverageStatus Status);
