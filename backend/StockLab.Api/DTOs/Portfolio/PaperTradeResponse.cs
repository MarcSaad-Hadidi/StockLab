using StockLab.Application.DTOs.Trading;
using System.Globalization;

namespace StockLab.Api.DTOs.Portfolio;

/// <summary>Public result of a paper-trading execution.</summary>
public sealed record PaperTradeResponse(
    Guid TransactionId,
    Guid OrderId,
    string Side,
    string Symbol,
    string Quantity,
    string ExecutionPrice,
    string TotalAmount,
    string CashBalance,
    string HoldingQuantity,
    string? AverageCost,
    DateTime ExecutedAtUtc,
    string OrderType,
    string? LimitPrice)
{
    public static PaperTradeResponse From(PaperTradeResult result) => new(
        result.TransactionId,
        result.OrderId,
        result.Side,
        result.Symbol,
        result.Quantity.ToString("0.############################", CultureInfo.InvariantCulture),
        result.ExecutionPrice.ToString("0.############################", CultureInfo.InvariantCulture),
        result.TotalAmount.ToString("0.############", CultureInfo.InvariantCulture),
        result.CashBalance.ToString("0.############", CultureInfo.InvariantCulture),
        result.HoldingQuantity.ToString("0.############################", CultureInfo.InvariantCulture),
        result.AverageCost?.ToString("0.############################", CultureInfo.InvariantCulture),
        DateTime.SpecifyKind(result.ExecutedAtUtc, DateTimeKind.Utc),
        result.OrderType,
        result.LimitPrice?.ToString("0.############################", CultureInfo.InvariantCulture));
}
