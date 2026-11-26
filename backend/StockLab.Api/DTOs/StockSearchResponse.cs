namespace StockLab.Api.DTOs;

/// <summary>An instrument returned by Market search or discovery.</summary>
public sealed record StockSearchResponse(
    string Symbol,
    string CompanyName,
    string? Exchange,
    string? Currency,
    string? InstrumentType);
