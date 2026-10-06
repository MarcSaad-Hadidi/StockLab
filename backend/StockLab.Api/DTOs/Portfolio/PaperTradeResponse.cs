using StockLab.Application.DTOs.Trading;
using System.Globalization;

namespace StockLab.Api.DTOs.Portfolio;

/// <summary>Public result of a paper-trading execution.</summary>
public sealed record PaperTradeResponse(
    Guid TransactionId,
    Guid OrderId,
    string Side,
    string Symbol,
    decimal Quantity,
    decimal ExecutionPrice,
    string TotalAmount,
    decimal CashBalance,
    decimal HoldingQuantity,
    decimal? AverageCost,
    DateTime ExecutedAtUtc,
    string OrderType,
    decimal? LimitPrice)
{
    public static PaperTradeResponse From(PaperTradeResult result) => new(
        result.TransactionId,
        result.OrderId,
        result.Side,
        result.Symbol,
        result.Quantity,
        result.ExecutionPrice,
        result.TotalAmount.ToString("0.############", CultureInfo.InvariantCulture),
        result.CashBalance,
        result.HoldingQuantity,
        result.AverageCost,
        DateTime.SpecifyKind(result.ExecutedAtUtc, DateTimeKind.Utc),
        result.OrderType,
        result.LimitPrice);
}
