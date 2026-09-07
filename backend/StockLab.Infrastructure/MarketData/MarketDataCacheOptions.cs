namespace StockLab.Infrastructure.MarketData;

public sealed class MarketDataCacheOptions
{
    public const string SectionName = "MarketDataCache";
    public long SizeLimit { get; set; } = 8 * 1024 * 1024;
    public TimeSpan QuoteTtl { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan SearchTtl { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan HistoryTtl { get; set; } = TimeSpan.FromMinutes(15);

    public bool HasValidTtls() => QuoteTtl > TimeSpan.Zero
        && SearchTtl > TimeSpan.Zero && HistoryTtl > TimeSpan.Zero
        && QuoteTtl <= TimeSpan.FromDays(365)
        && SearchTtl <= TimeSpan.FromDays(365) && HistoryTtl <= TimeSpan.FromDays(365);
}
