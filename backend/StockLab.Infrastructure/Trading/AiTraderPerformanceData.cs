using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

internal static class AiTraderPerformanceData
{
    internal static async Task<AiTraderPortfolioSnapshot> ValueAsync(IDbContextFactory<StockLabDbContext> factory,
        IAiTraderPortfolioService service, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        if (!await db.AiTraderPortfolios.AnyAsync(p => p.PortfolioKey == AiTraderPortfolio.MainPortfolioKey, token))
            throw new AiTraderPerformanceException(AiTraderPerformanceFailure.PortfolioNotFound);
        var snapshot = await service.GetSnapshotAsync(token, initializeIfMissing: false);
        token.ThrowIfCancellationRequested();
        return snapshot;
    }

    // Call under a short serializable transaction, after all provider I/O has completed.
    internal static async Task<AiTraderPortfolio> VerifyAsync(StockLabDbContext db, AiTraderPortfolioSnapshot snapshot, CancellationToken token)
    {
        var current = await db.AiTraderPortfolios.AsNoTracking().Include(p => p.Positions)
            .SingleOrDefaultAsync(p => p.PortfolioKey == AiTraderPortfolio.MainPortfolioKey, token);
        if (current is null || current.Id != snapshot.PortfolioId || current.Currency != snapshot.Currency
            || current.InitialCapital != snapshot.InitialCapital || current.CashBalance != snapshot.CashBalance
            || current.UpdatedAtUtc != snapshot.PortfolioUpdatedAtUtc || !current.Version.SequenceEqual(snapshot.PortfolioVersion)
            || !current.Positions.OrderBy(p => p.Symbol, StringComparer.Ordinal).Select(p => (p.Symbol, p.Quantity, p.AverageCost))
                .SequenceEqual(snapshot.Positions.OrderBy(p => p.Symbol, StringComparer.Ordinal).Select(p => (p.Symbol, p.Quantity, p.AverageCost))))
            throw new AiTraderPerformanceException(AiTraderPerformanceFailure.ConcurrencyConflict);
        return current;
    }

    internal static bool IsDatabaseFailure(Exception exception) => exception is DbUpdateException or DbException
        || exception is InvalidOperationException && exception.GetBaseException() is DbException;

    internal static AiTraderPerformanceException DatabaseFailure(Exception exception) =>
        new(exception is DbUpdateConcurrencyException || exception.GetBaseException() is SqlException { Number: 1205 or 2601 or 2627 }
            ? AiTraderPerformanceFailure.ConcurrencyConflict : AiTraderPerformanceFailure.PersistenceFailure);
}
