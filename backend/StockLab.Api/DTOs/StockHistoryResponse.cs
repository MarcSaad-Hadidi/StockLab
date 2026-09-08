namespace StockLab.Api.DTOs;

public sealed record StockHistoryResponse(
    string Symbol,
    string Currency,
    string Interval,
    StockHistoryBarResponse[] Bars);

public sealed record StockHistoryBarResponse(
    DateTimeOffset? OpenTimeUtc,
    DateOnly? PeriodDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long? Volume);
