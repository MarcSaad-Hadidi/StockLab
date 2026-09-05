namespace StockLab.Api.DTOs;

/// <summary>The public HTTP representation of a stock quote.</summary>
public sealed record StockQuoteResponse(
    string Symbol,
    decimal Price,
    decimal? Change,
    decimal? ChangePercent,
    long? Volume);
