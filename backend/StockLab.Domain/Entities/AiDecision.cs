namespace StockLab.Domain.Entities;

/// <summary>Original ML output only. Risk outcomes and executions belong to separate records.</summary>
public sealed class AiDecision
{
    public Guid Id { get; init; }
    public string Symbol { get; init; } = string.Empty;
    // Controlled BUY/SELL/HOLD storage keeps Domain independent of Application's enum.
    public string Signal { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
    public DateOnly DecisionDate { get; init; }
    public string ModelName { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public DateTime RecordedAtUtc { get; init; }
}
