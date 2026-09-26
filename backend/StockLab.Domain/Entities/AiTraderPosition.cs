namespace StockLab.Domain.Entities;

public sealed class AiTraderPosition
{
    public Guid Id { get; set; }
    public Guid AiTraderPortfolioId { get; set; }
    public AiTraderPortfolio Portfolio { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
