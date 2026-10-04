namespace StockLab.Application.Alerts;

/// <summary>
/// One cycle. QuoteCount counts non-null quotes, FailedQuoteCount counts null/failed lookups,
/// and DeferredSymbolCount counts symbols skipped after the daily monitoring budget is spent.
/// </summary>
public sealed record PriceAlertMonitoringResult(
    int ActiveAlertCount,
    int DistinctSymbolCount,
    int QuoteCount,
    int FailedQuoteCount,
    IReadOnlyList<PriceAlertMatch> Matches,
    int DeferredSymbolCount = 0);
