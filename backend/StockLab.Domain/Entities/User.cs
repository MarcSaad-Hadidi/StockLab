namespace StockLab.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] Version { get; set; } = [];

    public Portfolio? Portfolio { get; set; }
    public ICollection<Watchlist> Watchlists { get; set; } = [];
    public ICollection<PriceAlert> PriceAlerts { get; set; } = [];
}
