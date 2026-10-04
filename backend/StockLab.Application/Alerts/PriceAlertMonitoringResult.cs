namespace StockLab.Application.Alerts;

/// <summary>One monitoring cycle. QuoteCount counts non-null quotes; FailedQuoteCount counts null or failed lookups.</summary>
public sealed record PriceAlertMonitoringResult(
    int ActiveAlertCount,
    int DistinctSymbolCount,
    int QuoteCount,
    int FailedQuoteCount,
    IReadOnlyList<PriceAlertMatch> Matches);
