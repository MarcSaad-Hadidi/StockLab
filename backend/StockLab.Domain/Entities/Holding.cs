namespace StockLab.Domain.Entities;

public sealed class Holding
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
