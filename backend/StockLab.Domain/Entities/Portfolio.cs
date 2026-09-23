namespace StockLab.Domain.Entities;

public sealed class Portfolio
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Currency { get; set; } = "USD";
    public decimal InitialCapital { get; set; } = 100000m;
    public decimal CashBalance { get; set; } = 100000m;
    public DateTime CreatedAtUtc { get; set; }
    public byte[] Version { get; set; } = [];

    public ICollection<Holding> Holdings { get; set; } = [];
    public ICollection<Transaction> Transactions { get; set; } = [];
}
