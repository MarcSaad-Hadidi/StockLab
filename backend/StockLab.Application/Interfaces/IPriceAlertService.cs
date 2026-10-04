using StockLab.Domain.Entities;

namespace StockLab.Application.Interfaces;

public interface IPriceAlertService
{
    Task<IReadOnlyList<PriceAlert>> GetAllAsync(Guid userId, CancellationToken cancellationToken);
    Task<PriceAlert> CreateAsync(Guid userId, string symbol, string condition, decimal targetPrice, CancellationToken cancellationToken);
    Task<PriceAlert?> UpdateAsync(Guid userId, Guid alertId, string condition, decimal targetPrice, CancellationToken cancellationToken);
    Task<PriceAlert?> DisableAsync(Guid userId, Guid alertId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid userId, Guid alertId, CancellationToken cancellationToken);
}
