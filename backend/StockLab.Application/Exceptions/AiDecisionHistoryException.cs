namespace StockLab.Application.Exceptions;

public enum AiDecisionHistoryFailure { DecisionConflict, PersistenceFailure }

/// <summary>Safe history failure without database or connection details.</summary>
public sealed class AiDecisionHistoryException(AiDecisionHistoryFailure category)
    : Exception(category == AiDecisionHistoryFailure.DecisionConflict
        ? "The decision ID already belongs to a different ML decision."
        : "The AI decision history could not be accessed.")
{
    public AiDecisionHistoryFailure Category { get; } = category;
}
