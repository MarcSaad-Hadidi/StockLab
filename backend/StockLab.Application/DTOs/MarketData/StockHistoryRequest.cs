namespace StockLab.Application.DTOs.MarketData;

/// <summary>Provider-neutral, half-open history bounds. The range kind must match the interval.</summary>
public abstract record StockHistoryRange;
public sealed record IntradayHistoryRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : StockHistoryRange;
public sealed record CalendarHistoryRange(DateOnly FromDate, DateOnly ToDate) : StockHistoryRange;

/// <summary>Minute/Hour use UTC instants; Day/Week/Month use provider period dates, never synthetic instants.</summary>
public sealed record StockHistoryRequest(string Symbol, StockHistoryRange Range, StockHistoryInterval Interval)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Symbol);
        var valid = Range switch
        {
            IntradayHistoryRange r => Interval is StockHistoryInterval.Minute or StockHistoryInterval.Hour
                && r.FromUtc.Offset == TimeSpan.Zero && r.ToUtc.Offset == TimeSpan.Zero && r.FromUtc < r.ToUtc,
            CalendarHistoryRange r => Interval is StockHistoryInterval.Day or StockHistoryInterval.Week or StockHistoryInterval.Month
                && r.FromDate < r.ToDate,
            _ => false
        };
        if (!valid) throw new ArgumentException("History bounds must be ordered and match the interval.", nameof(Range));
    }
}
