namespace StockLab.Application.DTOs.MarketData;

/// <summary>
/// Historical bars for a canonical symbol and requested interval in one ISO 4217 currency.
/// Bars is never null; entries have unique opening timestamps in ascending order.
/// Market closures produce gaps, not synthesized bars. Prices are unadjusted for
/// splits and dividends so implementations cannot silently mix adjustment conventions.
/// </summary>
public sealed record StockHistory(
    string Symbol,
    string Currency,
    StockHistoryInterval Interval,
    IReadOnlyList<StockHistoryBar> Bars);
