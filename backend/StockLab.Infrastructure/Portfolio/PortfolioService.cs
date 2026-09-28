using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.Portfolio;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Portfolios;

public sealed class PortfolioService(StockLabDbContext dbContext) : IPortfolioService
{
    public async Task<PortfolioSummary?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        var portfolio = await dbContext.Portfolios
            .AsNoTracking()
            .Include(value => value.Holdings)
            .SingleOrDefaultAsync(value => value.UserId == userId, cancellationToken);
        if (portfolio is null)
        {
            return null;
        }

        var positions = portfolio.Holdings
            .OrderBy(holding => holding.Symbol)
            .Select(holding => new PortfolioPositionSummary(
                holding.Symbol,
                holding.Quantity,
                holding.AverageCost,
                decimal.Round(holding.Quantity * holding.AverageCost, 4, MidpointRounding.AwayFromZero)))
            .ToArray();
        var investedValue = decimal.Round(
            positions.Sum(position => position.Value), 4, MidpointRounding.AwayFromZero);
        var totalValue = decimal.Round(
            portfolio.CashBalance + investedValue, 4, MidpointRounding.AwayFromZero);

        return new PortfolioSummary(
            portfolio.Id,
            portfolio.UserId,
            portfolio.Currency,
            portfolio.InitialCapital,
            portfolio.CashBalance,
            investedValue,
            totalValue,
            positions);
    }
}
