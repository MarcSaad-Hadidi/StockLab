namespace StockLab.Application.DTOs.Portfolio;

public sealed record PortfolioSummary(
    Guid PortfolioId,
    Guid UserId,
    string Currency,
    decimal InitialCapital,
    decimal CashAvailable,
    decimal InvestedValue,
    decimal TotalValue,
    IReadOnlyList<PortfolioPositionSummary> Positions);

public sealed record PortfolioPositionSummary(
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    decimal Value);
