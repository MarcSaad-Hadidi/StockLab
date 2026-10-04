namespace StockLab.Application.Exceptions;

public enum AiRejectedDecisionHistoryFailure { DecisionNotFound, RejectionConflict, PersistenceFailure, DecisionAlreadyExecuted, ConcurrencyConflict }

/// <summary>Safe rejection history failure without database or connection details.</summary>
public sealed class AiRejectedDecisionHistoryException(AiRejectedDecisionHistoryFailure category)
    : Exception(category switch
    {
        AiRejectedDecisionHistoryFailure.DecisionNotFound => "The original AI decision does not exist.",
        AiRejectedDecisionHistoryFailure.RejectionConflict => "The decision already has a different rejection reason.",
        AiRejectedDecisionHistoryFailure.DecisionAlreadyExecuted => "The decision already has an executed trade.",
        AiRejectedDecisionHistoryFailure.ConcurrencyConflict => "The AI decision outcome changed concurrently.",
        _ => "The AI rejection history could not be accessed."
    })
{
    public AiRejectedDecisionHistoryFailure Category { get; } = category;
}
