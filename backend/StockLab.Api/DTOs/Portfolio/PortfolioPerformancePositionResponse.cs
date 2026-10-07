namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioPerformancePositionResponse(
    string Symbol,
    decimal Quantity,
    decimal AverageCost,
    decimal CurrentPrice,
    string MarketValue,
    string Pnl,
    decimal PnlPercent);
