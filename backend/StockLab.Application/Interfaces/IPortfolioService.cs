using StockLab.Application.DTOs.Portfolio;

namespace StockLab.Application.Interfaces;

public interface IPortfolioService
{
    /// <summary>Reads the authenticated user's portfolio, or null when none exists.</summary>
    Task<PortfolioSummary?> GetPortfolioAsync(Guid userId, CancellationToken cancellationToken);
}
