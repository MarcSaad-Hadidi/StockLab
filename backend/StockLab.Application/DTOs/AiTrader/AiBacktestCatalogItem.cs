namespace StockLab.Application.DTOs.AiTrader;

/// <summary>Persisted offline simulation; missing identity/statistics remain null. Rates are fractions.</summary>
public sealed record AiBacktestCatalogItem(string ModelName, string? ModelVersion, DateOnly StartDate,
    DateOnly EndDate, decimal InitialCapital, decimal FinalValue, decimal TotalReturn, decimal? WinRate,
    decimal MaxDrawdown, int? ExecutedTrades, decimal? BenchmarkReturn, decimal? BenchmarkMaxDrawdown,
    decimal? ReturnDelta, string EvaluationSource, DateTimeOffset? SourceTimestampUtc);
