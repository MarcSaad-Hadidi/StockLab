namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiTraderPortfolioSnapshot(
    Guid PortfolioId,
    string Currency,
    decimal InitialCapital,
    decimal CashBalance,
    decimal PositionsMarketValue,
    decimal TotalValue,
    decimal PnL,
    IReadOnlyList<AiTraderPositionSnapshot> Positions)
{
    public byte[] PortfolioVersion { get; init; } = [];
    public DateTime PortfolioUpdatedAtUtc { get; init; }
    public DateTime ValuationTimestampUtc { get; init; }
}

public sealed record AiTraderPositionSnapshot(
    string Symbol,
    decimal Quantity,
    decimal AverageCost,
    decimal CurrentPrice,
    decimal MarketValue,
    decimal UnrealizedPnL)
{
    public DateTimeOffset QuoteAsOfUtc { get; init; }
}
