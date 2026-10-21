namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiTraderPortfolioState(
    Guid PortfolioId,
    string Currency,
    decimal InitialCapital,
    decimal CashBalance,
    IReadOnlyList<AiTraderPositionState> Positions)
{
    public byte[] Version { get; init; } = [];
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed record AiTraderPositionState(string Symbol, decimal Quantity, decimal AverageCost);
