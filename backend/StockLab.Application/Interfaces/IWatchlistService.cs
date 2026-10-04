using StockLab.Domain.Entities;

namespace StockLab.Application.Interfaces;

public interface IWatchlistService
{
    Task<IReadOnlyList<Watchlist>> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<Watchlist> AddAsync(Guid userId, string symbol, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(Guid userId, string symbol, CancellationToken cancellationToken);
}
