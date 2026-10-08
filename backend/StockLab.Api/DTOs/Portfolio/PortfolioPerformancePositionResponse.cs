namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioPerformancePositionResponse(
    string Symbol,
    string Quantity,
    string AverageCost,
    string CurrentPrice,
    string MarketValue,
    string Pnl,
    decimal PnlPercent);
