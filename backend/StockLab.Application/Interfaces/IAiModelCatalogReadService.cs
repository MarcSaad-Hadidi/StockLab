using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Application.Interfaces;

public interface IAiModelCatalogReadService
{
    Task<IReadOnlyList<AiModelCatalogItem>> GetAsync(CancellationToken cancellationToken = default);
}
