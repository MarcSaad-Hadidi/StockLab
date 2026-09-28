using StockLab.Application.DTOs.Portfolio;

namespace StockLab.Application.Interfaces;

public interface IPortfolioService
{
    Task<PortfolioSummary?> GetAsync(Guid userId, CancellationToken cancellationToken);
}
