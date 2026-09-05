namespace StockLab.Application.DTOs.MarketData;

/// <summary>
/// Requested bar granularity, independent of provider-specific interval strings.
/// Day, week and month follow the instrument's exchange calendar, not fixed UTC durations.
/// </summary>
public enum StockHistoryInterval
{
    Minute = 1,
    Hour = 2,
    Day = 3,
    Week = 4,
    Month = 5
}
