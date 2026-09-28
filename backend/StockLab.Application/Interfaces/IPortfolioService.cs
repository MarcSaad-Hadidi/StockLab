using StockLab.Application.DTOs.Portfolio;

namespace StockLab.Application.Interfaces;

public interface IPortfolioService
{
    /// <summary>Reads the authenticated user's portfolio, or null when none exists.</summary>
    Task<PortfolioSummary?> GetPortfolioAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Gets the authenticated user's portfolio identifier, or null when none exists.</summary>
    Task<Guid?> GetPortfolioIdAsync(Guid userId, CancellationToken cancellationToken);
}
