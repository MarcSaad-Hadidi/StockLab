namespace StockLab.Application.DTOs.Trading;

/// <summary>Result of a paper-trading execution and the resulting portfolio state.</summary>
public sealed record PaperTradeResult(
    Guid TransactionId,
    Guid OrderId,
    Guid PortfolioId,
    string Side,
    string Symbol,
    decimal Quantity,
    decimal ExecutionPrice,
    decimal TotalAmount,
    decimal CashBalance,
    decimal HoldingQuantity,
    decimal? AverageCost,
    DateTime ExecutedAtUtc);
