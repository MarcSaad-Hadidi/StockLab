namespace StockLab.Domain.Entities;

public sealed class Transaction
{
    public Guid Id { get; set; }
    public Guid PortfolioId { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
    public Guid OrderId { get; set; }
    public string Side { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ExecutionPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
}
