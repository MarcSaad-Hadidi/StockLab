namespace StockLab.Infrastructure.Trading;

public sealed class AiPerformanceCycleOptions
{
    public const string SectionName = "AiPerformanceCycle";
    public bool Enabled { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public string? ExpectedDatabase { get; set; }
    public bool IsValid() => Interval >= TimeSpan.FromMinutes(1) && Interval <= TimeSpan.FromDays(1)
        && (!Enabled || !string.IsNullOrWhiteSpace(ExpectedDatabase));
}
