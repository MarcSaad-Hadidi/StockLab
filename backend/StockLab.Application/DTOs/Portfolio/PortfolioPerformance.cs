namespace StockLab.Application.DTOs.Portfolio;

/// <summary>Portfolio valuation calculated from the latest available market quotes.</summary>
public sealed record PortfolioPerformance(
    decimal CashBalance,
    decimal InitialCapital,
    decimal InvestedValue,
    decimal PositionsMarketValue,
    decimal TotalValue,
    decimal TotalPnl,
    decimal ReturnPercent,
    string Currency,
    IReadOnlyList<PortfolioPerformancePosition> Positions);

public sealed record PortfolioPerformancePosition(
    string Symbol,
    decimal Quantity,
    decimal AverageCost,
    decimal CurrentPrice,
    decimal MarketValue,
    decimal Pnl,
    decimal PnlPercent);
