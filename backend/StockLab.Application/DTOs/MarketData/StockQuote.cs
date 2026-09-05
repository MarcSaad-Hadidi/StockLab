namespace StockLab.Application.DTOs.MarketData;

/// <summary>A quote in the stated ISO 4217 currency, using the source's observation time.</summary>
/// <param name="Symbol">Canonical stock identifier.</param>
/// <param name="Currency">ISO 4217 currency code for Price and Change.</param>
/// <param name="Price">Latest available price, not necessarily a live executable price.</param>
/// <param name="Change">Price change from the previous trading session's close, or null if unavailable.</param>
/// <param name="ChangePercent">Change in percentage points: 1.5 means 1.5%, or null if unavailable.</param>
/// <param name="Volume">Cumulative share volume for the quoted trading session, or null if unavailable.</param>
/// <param name="AsOfUtc">Quote observation timestamp with a zero UTC offset, not the retrieval time.</param>
public sealed record StockQuote(
    string Symbol,
    string Currency,
    decimal Price,
    decimal? Change,
    decimal? ChangePercent,
    long? Volume,
    DateTimeOffset AsOfUtc);
