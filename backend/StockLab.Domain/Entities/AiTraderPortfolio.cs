namespace StockLab.Domain.Entities;

public sealed class AiTraderPortfolio
{
    public const string MainPortfolioKey = "AI_TRADER";
    public const decimal AiTraderInitialCapital = 100000m;
    public const string DefaultCurrency = "USD";

    public Guid Id { get; set; }
    public string PortfolioKey { get; set; } = MainPortfolioKey;
    public string Currency { get; set; } = DefaultCurrency;
    public decimal InitialCapital { get; set; } = AiTraderInitialCapital;
    public decimal CashBalance { get; set; } = AiTraderInitialCapital;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] Version { get; set; } = [];
    public ICollection<AiTraderPosition> Positions { get; set; } = [];
}
