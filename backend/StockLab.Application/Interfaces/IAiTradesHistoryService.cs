using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

/// <summary>Read-only history of committed AI paper trades; legacy rows may have no decision.</summary>
public interface IAiTradesHistoryService
{
    Task<AiTradeHistoryItem?> GetByIdAsync(Guid tradeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiTradeHistoryItem>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
