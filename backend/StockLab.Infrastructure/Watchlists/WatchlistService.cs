using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Watchlists;

public sealed class WatchlistService(StockLabDbContext dbContext, TimeProvider timeProvider) : IWatchlistService
{
    public async Task<IReadOnlyList<Watchlist>> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Watchlists.AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenBy(item => item.Symbol)
            .ToArrayAsync(cancellationToken);

    public async Task<Watchlist> AddAsync(Guid userId, string symbol, CancellationToken cancellationToken)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        if (await ExistsAsync(userId, normalizedSymbol, cancellationToken))
        {
            throw new WatchlistItemAlreadyExistsException();
        }

        var item = new Watchlist
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Symbol = normalizedSymbol,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };
        dbContext.Watchlists.Add(item);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException
            && (!dbContext.Database.IsSqlServer()
                || exception.GetBaseException() is SqlException { Number: 2601 or 2627 }))
        {
            // The unique index arbitrates inserts that both passed the precheck.
            // Confirm the same user/symbol won; unrelated failures still propagate.
            dbContext.Entry(item).State = EntityState.Detached;
            if (await ExistsAsync(userId, normalizedSymbol, cancellationToken))
            {
                throw new WatchlistItemAlreadyExistsException();
            }

            throw;
        }

        return item;
    }

    public async Task<bool> RemoveAsync(Guid userId, string symbol, CancellationToken cancellationToken)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        var item = await dbContext.Watchlists.SingleOrDefaultAsync(
            item => item.UserId == userId && item.Symbol == normalizedSymbol, cancellationToken);
        if (item is null)
        {
            return false;
        }

        dbContext.Watchlists.Remove(item);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request already removed the row after our lookup.
            dbContext.Entry(item).State = EntityState.Detached;
            return false;
        }
        return true;
    }

    private Task<bool> ExistsAsync(Guid userId, string symbol, CancellationToken cancellationToken) =>
        dbContext.Watchlists.AsNoTracking().AnyAsync(
            item => item.UserId == userId && item.Symbol == symbol, cancellationToken);

    private static string NormalizeSymbol(string symbol)
    {
        var normalized = symbol?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 32 || normalized.Contains('/'))
        {
            throw new ArgumentException("Symbol must contain between 1 and 32 characters without slashes.", nameof(symbol));
        }

        return normalized;
    }
}
