namespace StockLab.Infrastructure.Portfolios;

public sealed class PortfolioPerformanceCaptureOptions
{
    public const string SectionName = "PortfolioPerformanceCapture";
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public bool IsValid() => Interval >= TimeSpan.FromMinutes(1) && Interval <= TimeSpan.FromDays(1);
}
