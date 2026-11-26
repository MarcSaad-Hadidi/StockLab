namespace StockLab.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? GoogleSubject { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public bool EmailNotifications { get; set; }
    public bool TradeAlerts { get; set; }
    public bool MarketingCommunications { get; set; }
    public bool DarkMode { get; set; }
    public long SessionVersion { get; set; }
    public byte[]? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetExpiresAtUtc { get; set; }
    public DateTime? PasswordResetRequestedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] Version { get; set; } = [];

    public Portfolio? Portfolio { get; set; }
    public ICollection<Watchlist> Watchlists { get; set; } = [];
    public ICollection<PriceAlert> PriceAlerts { get; set; } = [];
}
