namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioTransactionResponse(
    Guid Id,
    string Side,
    string Symbol,
    string Quantity,
    string ExecutionPrice,
    string TotalAmount,
    DateTime ExecutedAtUtc);
