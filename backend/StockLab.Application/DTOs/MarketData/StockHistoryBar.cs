namespace StockLab.Application.DTOs.MarketData;

/// <summary>
/// An unadjusted OHLCV bar. Prices use the containing history's currency.
/// OpenTimeUtc is the bar's opening timestamp with a zero UTC offset.
/// Volume is the number of shares traded during the bar, or null if unavailable.
/// Low must not exceed Open, Close or High; High must not be below Open or Close.
/// A bar still in progress may change on subsequent retrievals.
/// </summary>
public sealed record StockHistoryBar(
    DateTimeOffset OpenTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long? Volume);
