namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioResponse(
    string CashBalance,
    string InitialCapital,
    string InvestedValue,
    string TotalValue,
    string Currency,
    PortfolioPositionResponse[] Positions);
