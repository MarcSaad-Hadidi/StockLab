using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

/// <summary>Reads executed, open AI positions without initializing or modifying the portfolio.</summary>
public interface IAiCurrentPositionsService
{
    /// <summary>
    /// Returns positions in ordinal ascending symbol order; an existing empty portfolio returns an empty list.
    /// Uses the portfolio snapshot valuation. A missing portfolio or invalid/currency-mismatched quote
    /// fails with InvalidOperationException; provider failures and cancellation propagate without partial results.
    /// </summary>
    Task<IReadOnlyList<AiCurrentPosition>> GetCurrentAsync(CancellationToken cancellationToken = default);
}
