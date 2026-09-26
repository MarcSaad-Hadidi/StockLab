namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioPositionResponse(string Symbol, decimal Quantity, decimal AverageCost);
