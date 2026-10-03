namespace StockLab.Application.DTOs.Portfolio;

/// <summary>Stored portfolio balances valued at acquisition cost, without live market prices.</summary>
public sealed record PortfolioSummary(
    decimal CashBalance,
    decimal InitialCapital,
    decimal InvestedValue,
    decimal TotalValue,
    string Currency,
    IReadOnlyList<PortfolioPosition> Positions);

public sealed record PortfolioPosition(string Symbol, decimal Quantity, decimal AverageCost);

public sealed record PortfolioTransaction(
    Guid Id,
    string Side,
    string Symbol,
    decimal Quantity,
    decimal ExecutionPrice,
    decimal TotalAmount,
    DateTime ExecutedAtUtc);
