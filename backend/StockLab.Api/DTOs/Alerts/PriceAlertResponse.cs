using System.Globalization;

namespace StockLab.Api.DTOs.Alerts;

public sealed record PriceAlertResponse(Guid Id, string Symbol, string Currency, string Condition, decimal TargetPrice,
    string Status, decimal? TriggeredPrice, DateTime? TriggeredAtUtc, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public string TargetPriceDecimal => TargetPrice.ToString("0.####", CultureInfo.InvariantCulture);
    public string? TriggeredPriceDecimal => TriggeredPrice?.ToString("0.####", CultureInfo.InvariantCulture);
}
