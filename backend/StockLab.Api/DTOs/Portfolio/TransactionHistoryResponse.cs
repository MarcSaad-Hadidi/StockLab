namespace StockLab.Api.DTOs.Portfolio;

public sealed record TransactionHistoryResponse(
    PortfolioTransactionResponse[] Items, int Page, int PageSize, int TotalCount,
    string Currency, TransactionHistorySummaryResponse Summary);

public sealed record TransactionHistorySummaryResponse(
    int TotalTrades,
    string TotalInvested,
    string TotalProceeds);
