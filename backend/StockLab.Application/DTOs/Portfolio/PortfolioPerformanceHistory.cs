namespace StockLab.Application.DTOs.Portfolio;

public sealed record PortfolioPerformanceHistory(string Range, string Currency, DateTime? FromUtc,
    DateTime ToUtc, int SampleCount, string Resolution, DateTime? FirstRecordedAtUtc, DateTime? LastRecordedAtUtc,
    IReadOnlyList<PortfolioPerformanceHistoryPoint> Points);

public sealed record PortfolioPerformanceHistoryPoint(DateTime RecordedAtUtc, decimal TotalValue,
    string Source, DateTime? QuoteAsOfUtc);
