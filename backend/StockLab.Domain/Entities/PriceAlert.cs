namespace StockLab.Domain.Entities;

public sealed class PriceAlert
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public string Condition { get; set; } = string.Empty;
    public decimal TargetPrice { get; set; }
    public string Status { get; set; } = "Active";
    public decimal? TriggeredPrice { get; set; }
    public DateTime? TriggeredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] Version { get; set; } = [];
}
