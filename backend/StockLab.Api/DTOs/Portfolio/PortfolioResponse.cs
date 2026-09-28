namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioResponse(
    Guid PortfolioId,
    string Currency,
    decimal InitialCapital,
    decimal CashAvailable,
    decimal InvestedValue,
    decimal TotalValue,
    IReadOnlyList<PortfolioPositionResponse> Positions);

public sealed record PortfolioPositionResponse(
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    decimal Value);
