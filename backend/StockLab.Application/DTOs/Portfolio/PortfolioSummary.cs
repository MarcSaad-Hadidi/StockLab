namespace StockLab.Application.DTOs.Portfolio;

/// <summary>Stored portfolio balances valued at acquisition cost, without live market prices.</summary>
public sealed record PortfolioSummary(
    decimal CashBalance,
    decimal InvestedValue,
    decimal TotalValue,
    string Currency,
    IReadOnlyList<PortfolioPosition> Positions);

public sealed record PortfolioPosition(string Symbol, decimal Quantity, decimal AverageCost);
