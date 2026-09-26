using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.Portfolio;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Portfolios;

public sealed class PortfolioService(StockLabDbContext dbContext) : IPortfolioService
{
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
}
