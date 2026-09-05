namespace StockLab.Application.DTOs.MarketData;

/// <summary>
/// Requests bars whose opening timestamps fall in [FromUtc, ToUtc).
/// FromUtc must precede ToUtc; both timestamps must have a zero UTC offset.
/// Symbol must be nonblank and Interval must be a defined enum value.
/// Implementations validate these preconditions before retrieving data.
/// </summary>
public sealed record StockHistoryRequest(
    string Symbol,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    StockHistoryInterval Interval);
