namespace StockLab.Domain.Entities;

/// <summary>An immutable personal-account valuation observed at a real capture time.</summary>
public sealed class PortfolioValuationSnapshot
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public DateTime SlotUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public decimal CashBalance { get; set; }
    public decimal PositionsValue { get; set; }
    public decimal TotalValue { get; set; }
    public decimal InitialCapital { get; set; }
    public string Currency { get; set; } = "USD";
    public string Source { get; set; } = "Cash";
    public DateTime? QuoteAsOfUtc { get; set; }
    public byte[] PortfolioVersion { get; set; } = [];
    public string QuoteObservationsJson { get; set; } = "[]";
}
