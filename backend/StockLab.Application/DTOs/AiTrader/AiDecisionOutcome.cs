namespace StockLab.Application.DTOs.AiTrader;

public sealed record AiDecisionOutcome(string ExecutionStatus, string? RejectionReason, Guid? TradeId);
