namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioPositionResponse(string Symbol, string Quantity, string AverageCost);
