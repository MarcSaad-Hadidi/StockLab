namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiTraderPortfolioState(
    Guid PortfolioId,
    string Currency,
    decimal InitialCapital,
    decimal CashBalance,
    IReadOnlyList<AiTraderPositionState> Positions);

public sealed record AiTraderPositionState(string Symbol, decimal Quantity, decimal AverageCost);
