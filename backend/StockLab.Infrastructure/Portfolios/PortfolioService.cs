using System.Data;
using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.Portfolio;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Portfolios;

public sealed class PortfolioService(StockLabDbContext dbContext) : IPortfolioService
{
    public Task<Guid?> GetPortfolioIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.UserId == userId)
            .Select(portfolio => (Guid?)portfolio.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<string?> GetPortfolioCurrencyAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.UserId == userId)
            .Select(portfolio => portfolio.Currency)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PortfolioSummary?> GetPortfolioAsync(Guid userId, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .Include(portfolio => portfolio.Holdings)
            .SingleOrDefaultAsync(portfolio => portfolio.UserId == userId, cancellationToken);
        if (portfolio is null)
        {
            return null;
        }

        var positions = portfolio.Holdings
            .OrderBy(holding => holding.Symbol, StringComparer.Ordinal)
            .Select(holding => new PortfolioPosition(holding.Symbol, holding.Quantity, holding.AverageCost))
            .ToArray();
        var investedValue = positions.Sum(position => position.Quantity * position.AverageCost);

        return new PortfolioSummary(portfolio.CashBalance, portfolio.InitialCapital, investedValue,
            portfolio.CashBalance + investedValue, portfolio.Currency, positions);
    }

    public async Task<TransactionHistory?> GetTransactionHistoryAsync(
        Guid userId, TransactionHistoryQuery query, CancellationToken cancellationToken)
    {
        // Keep counts, aggregates and rows consistent if a trade commits during this read.
        await using var readTransaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var portfolio = await dbContext.Portfolios.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new { row.Id, row.Currency })
            .SingleOrDefaultAsync(cancellationToken);
        if (portfolio is null) return null;

        var owned = dbContext.Transactions.AsNoTracking()
            .Where(row => row.PortfolioId == portfolio.Id);
        var summary = await owned.GroupBy(_ => 1)
            .Select(group => new TransactionHistorySummary(
                group.Count(),
                group.Sum(row => row.Side == "BUY" ? row.TotalAmount : 0m),
                group.Sum(row => row.Side == "SELL" ? row.TotalAmount : 0m)))
            .SingleOrDefaultAsync(cancellationToken) ?? new TransactionHistorySummary(0, 0m, 0m);

        var filtered = owned;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            filtered = filtered.Where(row => row.Symbol.ToUpper().Contains(search)
                || (row.RequestedSymbol != null && row.RequestedSymbol.ToUpper().Contains(search)));
        }
        if (query.Side is not null)
            filtered = filtered.Where(row => row.Side == query.Side);
        // Date-picker values and displayed transaction dates both use UTC days.
        if (query.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            filtered = filtered.Where(row => row.ExecutedAtUtc >= start);
        }
        if (query.To is { } to && to < DateOnly.MaxValue)
        {
            var endExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            filtered = filtered.Where(row => row.ExecutedAtUtc < endExclusive);
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 1 : (totalCount - 1) / query.PageSize + 1;
        var page = Math.Min(query.Page, totalPages);
        var items = await filtered.OrderByDescending(row => row.ExecutedAtUtc)
            .ThenByDescending(row => row.Id)
            .Skip((page - 1) * query.PageSize).Take(query.PageSize)
            .Select(row => new PortfolioTransaction(row.Id, row.Side, row.Symbol,
                row.Quantity, row.ExecutionPrice, row.TotalAmount, row.ExecutedAtUtc))
            .ToArrayAsync(cancellationToken);
        await readTransaction.CommitAsync(cancellationToken);
        return new TransactionHistory(items, page, query.PageSize, totalCount, portfolio.Currency, summary);
    }

    public async Task<IReadOnlyList<PortfolioTransaction>?> GetRecentTransactionsAsync(
        Guid userId, int limit, CancellationToken cancellationToken)
    {
        var portfolioId = await dbContext.Portfolios
            .AsNoTracking()
            .Where(portfolio => portfolio.UserId == userId)
            .Select(portfolio => (Guid?)portfolio.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (portfolioId is null)
            return null;

        return await dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.PortfolioId == portfolioId.Value)
            .OrderByDescending(transaction => transaction.ExecutedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Take(limit)
            .Select(transaction => new PortfolioTransaction(
                transaction.Id,
                transaction.Side,
                transaction.Symbol,
                transaction.Quantity,
                transaction.ExecutionPrice,
                transaction.TotalAmount,
                transaction.ExecutedAtUtc))
            .ToArrayAsync(cancellationToken);
    }
}
