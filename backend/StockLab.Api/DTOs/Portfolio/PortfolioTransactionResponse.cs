namespace StockLab.Api.DTOs.Portfolio;

public sealed record PortfolioTransactionResponse(
    Guid Id,
    string Side,
    string Symbol,
    decimal Quantity,
    decimal ExecutionPrice,
    decimal TotalAmount,
    DateTime ExecutedAtUtc);
