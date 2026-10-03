using StockLab.Application.DTOs.Portfolio;

namespace StockLab.Application.Interfaces;

public interface IPortfolioService
{
    /// <summary>Reads the authenticated user's portfolio, or null when none exists.</summary>
    Task<PortfolioSummary?> GetPortfolioAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Values the authenticated user's portfolio with current market quotes.</summary>
    Task<PortfolioPerformance?> GetPerformanceAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Gets the authenticated user's portfolio identifier, or null when none exists.</summary>
    Task<Guid?> GetPortfolioIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Gets the authenticated user's portfolio currency, or null when none exists.</summary>
    Task<string?> GetPortfolioCurrencyAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Reads the most recent transactions belonging to the authenticated user's portfolio.</summary>
    Task<IReadOnlyList<PortfolioTransaction>?> GetRecentTransactionsAsync(
        Guid userId, int limit, CancellationToken cancellationToken);

    /// <summary>Filters and pages the full owned history, with all-time totals and its currency.</summary>
    Task<TransactionHistory?> GetTransactionHistoryAsync(
        Guid userId, TransactionHistoryQuery query, CancellationToken cancellationToken);
}
