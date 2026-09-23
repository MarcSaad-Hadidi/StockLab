namespace StockLab.Domain.Entities;

public sealed class Watchlist
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Symbol { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
