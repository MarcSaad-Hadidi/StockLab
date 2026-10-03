namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioResponse(
    decimal CashBalance,
    decimal InitialCapital,
    decimal InvestedValue,
    decimal TotalValue,
    string Currency,
    PortfolioPositionResponse[] Positions);
