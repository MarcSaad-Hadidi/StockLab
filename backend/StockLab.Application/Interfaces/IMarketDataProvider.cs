using StockLab.Application.DTOs.MarketData;

namespace StockLab.Application.Interfaces;

/// <summary>
/// Provides market data independently of its source. Implementations belong in Infrastructure.
/// </summary>
/// <remarks>
/// Symbols and queries must be nonblank. Implementations normalize symbols and return
/// canonical identifiers, using exchange-qualified symbols when needed for uniqueness.
/// Invalid arguments fail with ArgumentException (including its derived types).
/// Cancellation propagates as OperationCanceledException. Retrieval failures fault the task;
/// they must not be represented as missing data or fabricated zero values.
/// </remarks>
public interface IMarketDataProvider
{
    /// <summary>Returns the latest available quote, or null if the symbol is unknown.</summary>
    Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches by ticker or company name. Returns an empty list when no stocks match,
    /// never null. Results contain unique canonical symbols.
    /// </summary>
    Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns historical bars, or null if the symbol is unknown. A known symbol with
    /// no bars in the requested range returns a history with an empty Bars collection.
    /// An unsupported interval fails with NotSupportedException; it is never silently changed.
    /// </summary>
    Task<StockHistory?> GetHistoryAsync(
        StockHistoryRequest request,
        CancellationToken cancellationToken = default);
}
