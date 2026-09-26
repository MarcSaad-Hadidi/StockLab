using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

/// <summary>Initializes and reads the independent, system-owned paper portfolio. Never executes trades.</summary>
public interface IAiTraderPortfolioService
{
    Task<AiTraderPortfolioState> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns persisted state, initializing on first use; requires no market data.</summary>
    /// <param name="initializeIfMissing">Set false for strictly read-only callers; a missing portfolio then fails.</param>
    Task<AiTraderPortfolioState> GetStateAsync(CancellationToken cancellationToken = default, bool initializeIfMissing = true);

    /// <summary>Values all positions using market quotes. Missing/invalid quotes or currency mismatches fail the entire snapshot.</summary>
    /// <param name="initializeIfMissing">Set false to prevent first-use initialization.</param>
    Task<AiTraderPortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default, bool initializeIfMissing = true);
}
