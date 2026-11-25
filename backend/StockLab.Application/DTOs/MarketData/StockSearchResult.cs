namespace StockLab.Application.DTOs.MarketData;

/// <summary>An instrument matching a symbol search or available in an asset catalog.</summary>
/// <param name="Symbol">Canonical identifier usable by quote and history operations.</param>
/// <param name="CompanyName">Display name of the instrument.</param>
/// <param name="Exchange">Exchange identifier, or null when unavailable.</param>
/// <param name="Currency">Quote currency code, or null when unavailable.</param>
/// <param name="InstrumentType">Instrument type reported by the market data provider.</param>
public sealed record StockSearchResult(
    string Symbol,
    string CompanyName,
    string? Exchange,
    string? Currency,
    string? InstrumentType = null);
