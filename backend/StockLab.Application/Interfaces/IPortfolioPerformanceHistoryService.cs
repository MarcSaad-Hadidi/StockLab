using StockLab.Application.DTOs.Portfolio;

namespace StockLab.Application.Interfaces;

public interface IPortfolioPerformanceHistoryService
{
    /// <summary>Reads only committed observations belonging to the authenticated account.</summary>
    Task<PortfolioPerformanceHistory?> GetAsync(Guid userId, string range, CancellationToken cancellationToken);
}
