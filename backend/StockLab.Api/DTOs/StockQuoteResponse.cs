namespace StockLab.Api.DTOs;

/// <summary>The public HTTP representation of a stock quote.</summary>
public sealed record StockQuoteResponse(
    string Symbol,
    decimal Price,
    decimal? Change,
    decimal? ChangePercent,
    long? Volume,
    string Currency,
    DateTimeOffset AsOfUtc,
    string? Name,
    string? Exchange,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? PreviousClose,
    long? AverageVolume,
    bool? IsMarketOpen,
    StockLab.Application.DTOs.MarketData.StockFiftyTwoWeek? FiftyTwoWeek)
{
    /// <summary>Lossless price for financial calculations; Price remains compatible with chart clients.</summary>
    public string PriceDecimal => Price.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
}
