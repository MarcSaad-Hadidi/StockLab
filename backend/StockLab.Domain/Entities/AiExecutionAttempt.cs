namespace StockLab.Domain.Entities;

/// <summary>Durable paper-execution intent and outcome; STARTED may require reconciliation after interruption.</summary>
public sealed class AiExecutionAttempt
{
    public Guid Id { get; set; }
    public Guid DecisionId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = "STARTED";
    public string? FailureCode { get; set; }
    public decimal? RequestedPrice { get; set; }
    public decimal? ApprovedQuantity { get; set; }
    public Guid? TradeId { get; set; }
    public bool IsReplay { get; set; }
}
