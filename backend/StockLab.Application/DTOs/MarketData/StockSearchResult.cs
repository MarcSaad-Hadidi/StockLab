namespace StockLab.Application.DTOs.MarketData;

/// <summary>A stock matching a ticker or company-name search.</summary>
/// <param name="Symbol">Canonical stock identifier usable by quote and history operations.</param>
/// <param name="CompanyName">Display name of the company.</param>
/// <param name="Exchange">Exchange identifier, or null when unavailable.</param>
/// <param name="Currency">ISO 4217 quote currency code, or null when unavailable.</param>
public sealed record StockSearchResult(
    string Symbol,
    string CompanyName,
    string? Exchange,
    string? Currency);
