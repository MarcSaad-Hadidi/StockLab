namespace StockLab.Application.DTOs.Portfolio;

public sealed record TransactionHistoryQuery(
    int Page, int PageSize, string? Search, string? Side, DateOnly? From, DateOnly? To);

/// <summary>All-time aggregates for the authenticated portfolio, independent of page filters.</summary>
public sealed record TransactionHistorySummary(int TotalTrades, decimal TotalInvested, decimal TotalProceeds);

public sealed record TransactionHistory(
    IReadOnlyList<PortfolioTransaction> Items, int Page, int PageSize, int TotalCount,
    string Currency, TransactionHistorySummary Summary);
