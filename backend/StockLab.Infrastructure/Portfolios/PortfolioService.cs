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

        return new PortfolioSummary(portfolio.CashBalance, investedValue,
            portfolio.CashBalance + investedValue, portfolio.Currency, positions);
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
