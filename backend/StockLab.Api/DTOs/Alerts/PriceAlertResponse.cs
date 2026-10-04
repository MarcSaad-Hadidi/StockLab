namespace StockLab.Api.DTOs.Alerts;

public sealed record PriceAlertResponse(Guid Id, string Symbol, string Currency, string Condition, decimal TargetPrice,
    string Status, decimal? TriggeredPrice, DateTime? TriggeredAtUtc, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
