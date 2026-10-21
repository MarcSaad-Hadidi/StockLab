namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiModelPeriod(DateOnly StartDate, DateOnly EndDate);

/// <summary>Registered training metadata, with status scoped to each model family.</summary>
public sealed record AiModelCatalogItem(string ModelName, string ModelVersion, string ModelType,
    Guid TrainingRunId, DateTimeOffset TrainedAtUtc, string Status, double Accuracy, double Precision,
    double Recall, double F1, AiModelPeriod TrainingPeriod, AiModelPeriod EvaluationPeriod);
