namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioPerformanceResponse(
    string CashBalance,
    string InitialCapital,
    string InvestedValue,
    string PositionsMarketValue,
    string TotalValue,
    string TotalPnl,
    decimal ReturnPercent,
    string Currency,
    PortfolioPerformancePositionResponse[] Positions);
