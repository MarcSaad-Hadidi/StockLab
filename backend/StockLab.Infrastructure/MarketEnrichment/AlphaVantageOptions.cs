namespace StockLab.Infrastructure.MarketEnrichment;
public sealed class AlphaVantageOptions
{
    public int DailyRequestBudget { get; set; } = 20;
    public int TimeoutSeconds { get; set; } = 10;
    public TimeSpan OverviewTtl { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan LogoTtl { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan EarningsTtl { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan MoversTtl { get; set; } = TimeSpan.FromHours(12);
    public bool IsValid() => DailyRequestBudget is > 0 and <= 25 && TimeoutSeconds is > 0 and <= 60 &&
        new[] { OverviewTtl, LogoTtl, EarningsTtl, MoversTtl }.All(t => t > TimeSpan.Zero && t <= TimeSpan.FromDays(365));
}
