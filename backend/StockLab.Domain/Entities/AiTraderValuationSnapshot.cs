namespace StockLab.Domain.Entities;

/// <summary>Append-only observation; quote times describe the source prices, not the capture time.</summary>
public sealed class AiTraderValuationSnapshot
{
    public Guid Id { get; init; }
    public Guid AiTraderPortfolioId { get; init; }
    public Guid CaptureId { get; init; }
    public DateTime ValuedAtUtc { get; init; }
    public decimal CashBalance { get; init; }
    public decimal PositionsValue { get; init; }
    public string Currency { get; init; } = "USD";
    public bool IsInitialBaseline { get; init; }
    public byte[] PortfolioVersion { get; init; } = [];
    public string QuoteObservationsJson { get; init; } = "[]";
}
