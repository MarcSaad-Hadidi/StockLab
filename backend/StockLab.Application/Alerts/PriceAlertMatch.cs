namespace StockLab.Application.Alerts;

/// <summary>A detected condition for the triggering service to consume; no state has been persisted.</summary>
public sealed record PriceAlertMatch(
    Guid AlertId,
    Guid UserId,
    string Symbol,
    string Currency,
    string Condition,
    decimal TargetPrice,
    decimal ObservedPrice,
    DateTimeOffset ObservedAtUtc);
