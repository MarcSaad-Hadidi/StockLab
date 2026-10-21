using StockLab.Application.DTOs.AiTrader;

namespace StockLab.Api.DTOs.AiTrader;

public sealed record AiTraderModelPeriodResponse(DateOnly StartDate, DateOnly EndDate);

public sealed record AiTraderModelResponse(string ModelName, string ModelVersion, string ModelType, Guid TrainingRunId,
    DateTime TrainedAtUtc, string Status, double Accuracy, double Precision, double Recall, double F1,
    AiTraderModelPeriodResponse TrainingPeriod, AiTraderModelPeriodResponse EvaluationPeriod)
{
    internal static AiTraderModelResponse From(AiModelCatalogItem value) => new(value.ModelName, value.ModelVersion,
        value.ModelType, value.TrainingRunId, value.TrainedAtUtc.UtcDateTime, value.Status, value.Accuracy, value.Precision,
        value.Recall, value.F1, new(value.TrainingPeriod.StartDate, value.TrainingPeriod.EndDate),
        new(value.EvaluationPeriod.StartDate, value.EvaluationPeriod.EndDate));
}

public sealed record AiTraderBacktestResponse(string ModelName, string? ModelVersion, DateOnly StartDate, DateOnly EndDate,
    string InitialCapital, string FinalValue, decimal TotalReturn, decimal? WinRate, decimal MaxDrawdown,
    int? ExecutedTrades, decimal? BenchmarkReturn, decimal? BenchmarkMaxDrawdown, decimal? ReturnDelta,
    string EvaluationSource, DateTime? SourceTimestampUtc)
{
    internal static AiTraderBacktestResponse From(AiBacktestCatalogItem value) => new(value.ModelName, value.ModelVersion,
        value.StartDate, value.EndDate, AiTraderValues.Amount(value.InitialCapital)!, AiTraderValues.Amount(value.FinalValue)!,
        value.TotalReturn, value.WinRate, value.MaxDrawdown, value.ExecutedTrades, value.BenchmarkReturn,
        value.BenchmarkMaxDrawdown, value.ReturnDelta, value.EvaluationSource, value.SourceTimestampUtc?.UtcDateTime);
}
