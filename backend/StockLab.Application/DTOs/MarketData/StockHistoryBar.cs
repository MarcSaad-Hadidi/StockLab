namespace StockLab.Application.DTOs.MarketData;

/// <summary>
/// Unadjusted OHLCV. Exactly one temporal value is present: a UTC instant for intraday,
/// or the source's period date for calendar bars. Volume is shares, null if unavailable.
/// </summary>
public sealed record StockHistoryBar
{
    public DateTimeOffset? OpenTimeUtc { get; }
    public DateOnly? PeriodDate { get; }
    public decimal Open { get; }
    public decimal High { get; }
    public decimal Low { get; }
    public decimal Close { get; }
    public long? Volume { get; }

    public StockHistoryBar(DateTimeOffset openTimeUtc, decimal open, decimal high, decimal low, decimal close, long? volume)
        : this(open, high, low, close, volume)
    {
        if (openTimeUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Timestamp must be UTC.", nameof(openTimeUtc));
        OpenTimeUtc = openTimeUtc;
    }
    public StockHistoryBar(DateOnly periodDate, decimal open, decimal high, decimal low, decimal close, long? volume)
        : this(open, high, low, close, volume) => PeriodDate = periodDate;

    private StockHistoryBar(decimal open, decimal high, decimal low, decimal close, long? volume)
    {
        if (open <= 0 || close <= 0 || low <= 0 || high <= 0 || low > open || low > close || high < open || high < close || low > high || volume < 0)
            throw new ArgumentException("Inconsistent OHLCV.");
        Open = open; High = high; Low = low; Close = close; Volume = volume;
    }
}
