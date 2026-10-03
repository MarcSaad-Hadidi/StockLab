namespace StockLab.Domain.Entities;

/// <summary>Append-only risk rejection of one previously recorded ML decision.</summary>
public sealed class AiRejectedDecision
{
    public Guid AiDecisionId { get; init; }
    public string RejectionReason { get; init; } = string.Empty;
    public DateTime RejectedAtUtc { get; init; }
}
