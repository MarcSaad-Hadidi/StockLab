namespace StockLab.Application.DTOs.AiTrader;

/// <summary>An open AI position valued in portfolio currency. PnL is unrealized monetary profit/loss.</summary>
public sealed record AiCurrentPosition(
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    decimal CurrentPrice,
    decimal MarketValue,
    decimal PnL);
