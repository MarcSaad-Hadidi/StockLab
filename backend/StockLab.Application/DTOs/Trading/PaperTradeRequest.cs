namespace StockLab.Application.DTOs.Trading;

/// <summary>Validated input for one paper-trading execution.</summary>
public sealed record PaperTradeRequest(
    Guid OrderId,
    string Side,
    string Symbol,
    decimal Quantity,
    decimal ExecutionPrice);
