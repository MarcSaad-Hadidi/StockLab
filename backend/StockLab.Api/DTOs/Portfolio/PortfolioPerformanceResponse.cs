namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioPerformanceResponse(
    decimal CashBalance,
    decimal InitialCapital,
    decimal InvestedValue,
    decimal PositionsMarketValue,
    decimal TotalValue,
    decimal TotalPnl,
    decimal ReturnPercent,
    string Currency,
    PortfolioPerformancePositionResponse[] Positions);
