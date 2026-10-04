namespace StockLab.Infrastructure.Alerts;

public sealed class PriceAlertMonitoringOptions
{
    public const string SectionName = "PriceAlertMonitoring";
    public const string ValidationMessage = "PriceAlertMonitoring:Interval must be at least 1 millisecond and at most 4294967294 milliseconds.";
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    public bool IsValid() => Interval >= TimeSpan.FromMilliseconds(1)
        && Interval <= TimeSpan.FromMilliseconds(uint.MaxValue - 1);
}
