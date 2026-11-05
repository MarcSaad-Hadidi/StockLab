using System.ComponentModel.DataAnnotations;

namespace StockLab.Api.DTOs.Portfolio;

public sealed class PortfolioPerformanceHistoryRequest
{
    [Required, RegularExpression("^(1D|1W|1M|3M|1Y|YTD|ALL)$")]
    public string Range { get; set; } = "1M";
}

public sealed record PortfolioPerformanceHistoryResponse(string Range, string Currency, DateTime? FromUtc,
    DateTime ToUtc, int SampleCount, string Resolution, DateTime? FirstRecordedAtUtc, DateTime? LastRecordedAtUtc,
    IReadOnlyList<PortfolioPerformanceHistoryPointResponse> Points);

public sealed record PortfolioPerformanceHistoryPointResponse(DateTime RecordedAtUtc, string TotalValue,
    string Source, DateTime? QuoteAsOfUtc);
