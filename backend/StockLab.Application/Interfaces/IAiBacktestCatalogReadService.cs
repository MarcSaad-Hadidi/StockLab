using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiBacktestCatalogReadService
{
    Task<IReadOnlyList<AiBacktestCatalogItem>> GetAsync(CancellationToken cancellationToken = default);
}
