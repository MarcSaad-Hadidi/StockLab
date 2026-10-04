namespace StockLab.Domain.Entities;

public sealed class AiTrade
{
    public Guid Id { get; set; }
    public Guid AiTraderPortfolioId { get; set; }
    public AiTraderPortfolio Portfolio { get; set; } = null!;
    public Guid OrderId { get; set; }
    // Null only for trades committed before decision links were introduced.
    public Guid? AiDecisionId { get; set; }
    public AiDecision? Decision { get; set; }
    // Canonical original approval, not the possibly reduced execution quantity or fresh price.
    public string OrderFingerprint { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ExecutionPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
    // Preserve the committed response even after subsequent orders change the portfolio.
    public decimal CashBalanceAfter { get; set; }
    public decimal PositionQuantityAfter { get; set; }
    public decimal? AverageCostAfter { get; set; }
}
