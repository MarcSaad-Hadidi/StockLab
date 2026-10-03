using StockLab.Application.DTOs.Portfolio;

namespace StockLab.Api.DTOs.Portfolio;

public sealed record TransactionHistoryResponse(
    PortfolioTransactionResponse[] Items, int Page, int PageSize, int TotalCount,
    string Currency, TransactionHistorySummary Summary);
