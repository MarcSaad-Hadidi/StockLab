using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

/// <summary>Initializes and reads the independent, system-owned paper portfolio. Never executes trades.</summary>
public interface IAiTraderPortfolioService
{
    Task<AiTraderPortfolioState> GetOrCreateAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns persisted state, initializing on first use; requires no market data.</summary>
    Task<AiTraderPortfolioState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Values all positions using market quotes. Missing/invalid quotes or currency mismatches fail the entire snapshot.</summary>
    Task<AiTraderPortfolioSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
