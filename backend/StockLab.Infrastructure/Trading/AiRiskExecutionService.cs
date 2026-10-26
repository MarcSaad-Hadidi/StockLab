using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public sealed class AiRiskExecutionService(IDbContextFactory<StockLabDbContext> factory,
    IAiDecisionHistoryService decisions, IAiDecisionOutcomeReadService outcomes, IAiRiskManager risk,
    IAiTradeExecutionService execution, AiRejectedDecisionHistoryService rejections,
    [FromKeyedServices("Execution")] IMarketDataProvider market, TimeProvider clock) : IAiRiskExecutionService
{
    public async Task<AiRiskExecutionResult> ExecuteAsync(Guid decisionId, CancellationToken cancellationToken = default)
    {
        if (decisionId == Guid.Empty) throw new ArgumentException("A persisted decision ID is required.", nameof(decisionId));
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await decisions.GetByIdAsync(decisionId, cancellationToken)
            ?? throw new AiTradeExecutionException(AiTradeExecutionFailure.DecisionNotFound);
        if (raw.Signal == AiTradingSignal.Hold) return new(decisionId, null, "HOLD", null, null, false);
        if (await TerminalAsync(decisionId, cancellationToken) is { } existing)
        {
            await RecoverAsync(existing, cancellationToken);
            return existing;
        }

        var attemptId = await StartAsync(decisionId, cancellationToken);
        AiRiskDecision? approval = null;
        AiRiskExecutionResult result;
        var stage = "QuoteUnavailable";
        try
        {
            var price = await AiPaperTradingEngine.PriceAsync(market, raw.Symbol, cancellationToken);
            stage = "RiskEvaluationFailed";
            approval = await risk.EvaluateAsync(new(raw.Symbol, raw.Signal, raw.Confidence, price), cancellationToken);
            stage = "InvalidRiskDecision";
            ValidateRisk(raw, approval, price);
            await SaveApprovalAsync(attemptId, approval, cancellationToken);
            stage = "ExecutionFailed";
            if (!approval.Approved)
            {
                var recorded = await rejections.RecordWithReplayAsync(new(decisionId, approval), cancellationToken);
                result = new(decisionId, null, "REJECTED", null, approval.RejectionReason!.Value.ToString(), recorded.IsReplay);
            }
            else
            {
                // One stable order per original decision, including after an interrupted attempt.
                var trade = await execution.ExecuteAsync(new(decisionId, decisionId, approval), cancellationToken);
                result = new(decisionId, null, "EXECUTED", trade.TradeId, null, trade.IsIdempotentReplay);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var actual = await TerminalAsync(decisionId, cleanup.Token)
                    ?? new(decisionId, null, "CANCELLED", null, "Cancelled", false);
                await CompleteAsync(attemptId, actual, cleanup.Token);
            }
            catch (Exception) { /* A durable STARTED row preserves uncertainty if cleanup cannot reach SQL. */ }
            throw;
        }
        catch (AiTradeExecutionException error)
        {
            var winner = await TerminalAsync(decisionId, cancellationToken);
            if (winner is not null) result = winner;
            else if (error.Category == AiTradeExecutionFailure.RiskChanged && error.RiskReason is { } reason && approval is not null)
            {
                var refused = approval with { Approved = false, ApprovedQuantity = 0m, RejectionReason = reason };
                try
                {
                    var recorded = await rejections.RecordWithReplayAsync(new(decisionId, refused), cancellationToken);
                    result = new(decisionId, null, "REJECTED", null, reason.ToString(), recorded.IsReplay);
                }
                catch (AiRejectedDecisionHistoryException conflict)
                {
                    result = await TerminalAsync(decisionId, cancellationToken)
                        ?? new(decisionId, null, "FAILED", null, conflict.Category.ToString(), false);
                }
            }
            else result = new(decisionId, null, "FAILED", null, error.Category.ToString(), false);
        }
        catch (AiRejectedDecisionHistoryException error)
        {
            result = await TerminalAsync(decisionId, cancellationToken)
                ?? new(decisionId, null, "FAILED", null, error.Category.ToString(), false);
        }
        catch (Exception)
        {
            result = await TerminalAsync(decisionId, cancellationToken)
                ?? new(decisionId, null, "FAILED", null, stage, false);
        }
        // Completion is outside the execution catch: a failed audit write must not relabel a committed trade FAILED.
        return await CompleteAsync(attemptId, result, cancellationToken);
    }

    private async Task<AiRiskExecutionResult?> TerminalAsync(Guid id, CancellationToken token)
    {
        var map = await outcomes.GetAsync([id], token);
        if (!map.TryGetValue(id, out var outcome)) throw new AiTradeExecutionException(AiTradeExecutionFailure.PersistenceFailure);
        if (outcome.ExecutionStatus == "EXECUTED" && outcome.TradeId is { } tradeId && tradeId != Guid.Empty)
            return new(id, null, "EXECUTED", tradeId, null, true);
        if (outcome.ExecutionStatus == "REJECTED" && Enum.TryParse<AiRiskRejectionReason>(outcome.RejectionReason, out var reason)
            && Enum.IsDefined(reason) && reason.ToString() == outcome.RejectionReason)
            return new(id, null, "REJECTED", null, outcome.RejectionReason, true);
        if (outcome.ExecutionStatus == "UNRESOLVED") return null;
        throw new AiTradeExecutionException(AiTradeExecutionFailure.PersistenceFailure);
    }

    private static void ValidateRisk(AiDecisionRecord raw, AiRiskDecision? approval, decimal price)
    {
        if (approval is null || approval.Symbol != raw.Symbol || approval.Signal != raw.Signal
            || approval.Confidence != raw.Confidence || approval.RequestedPrice != price
            || (approval.Approved
                ? approval.RejectionReason is not null || approval.ApprovedQuantity <= 0
                    || approval.ApprovedQuantity > AiRiskPolicyEvaluator.MaxStoredQuantity
                    || decimal.Round(approval.ApprovedQuantity, 8) != approval.ApprovedQuantity
                : approval.ApprovedQuantity != 0 || approval.RejectionReason is not { } reason || !Enum.IsDefined(reason)))
            throw new ArgumentException("Invalid risk outcome.");
    }

    private async Task<Guid> StartAsync(Guid decisionId, CancellationToken token)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            var attempt = new AiExecutionAttempt { Id = Guid.NewGuid(), DecisionId = decisionId,
                StartedAtUtc = clock.GetUtcNow().UtcDateTime };
            db.AiExecutionAttempts.Add(attempt);
            await db.SaveChangesAsync(token);
            return attempt.Id;
        }
        catch (Exception error) when (DatabaseFailure(error))
        { throw new AiTradeExecutionException(AiTradeExecutionFailure.PersistenceFailure); }
    }

    private async Task SaveApprovalAsync(Guid id, AiRiskDecision approval, CancellationToken token)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            if (await db.AiExecutionAttempts.Where(a => a.Id == id && a.Status == "STARTED").ExecuteUpdateAsync(set => set
                .SetProperty(a => a.RequestedPrice, (decimal?)approval.RequestedPrice)
                .SetProperty(a => a.ApprovedQuantity, (decimal?)approval.ApprovedQuantity), token) != 1)
                throw new AiTradeExecutionException(AiTradeExecutionFailure.ConcurrencyConflict);
        }
        catch (Exception error) when (DatabaseFailure(error))
        { throw new AiTradeExecutionException(AiTradeExecutionFailure.PersistenceFailure); }
    }

    private async Task<AiRiskExecutionResult> CompleteAsync(Guid id, AiRiskExecutionResult result, CancellationToken token)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            var updated = await db.AiExecutionAttempts.Where(a => a.Id == id && a.Status == "STARTED").ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Status, result.Status).SetProperty(a => a.FailureCode, result.FailureCode)
                .SetProperty(a => a.TradeId, result.TradeId).SetProperty(a => a.IsReplay, result.IsReplay)
                .SetProperty(a => a.CompletedAtUtc, (DateTime?)clock.GetUtcNow().UtcDateTime), token);
            if (updated == 1) return result with { AttemptId = id };
            if (await db.AiExecutionAttempts.AnyAsync(a => a.Id == id && a.Status != "STARTED", token)
                && await TerminalAsync(result.DecisionId, token) is { } winner)
                return winner with { AttemptId = id };
            throw new AiTradeExecutionException(AiTradeExecutionFailure.PersistenceFailure);
        }
        catch (Exception error) when (DatabaseFailure(error))
        { throw new AiTradeExecutionException(AiTradeExecutionFailure.PersistenceFailure); }
    }

    private async Task RecoverAsync(AiRiskExecutionResult outcome, CancellationToken token)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            await db.AiExecutionAttempts.Where(a => a.DecisionId == outcome.DecisionId && a.Status == "STARTED").ExecuteUpdateAsync(set => set
                .SetProperty(a => a.Status, outcome.Status).SetProperty(a => a.FailureCode, outcome.FailureCode)
                .SetProperty(a => a.TradeId, outcome.TradeId).SetProperty(a => a.IsReplay, true)
                .SetProperty(a => a.CompletedAtUtc, (DateTime?)clock.GetUtcNow().UtcDateTime), token);
        }
        catch (Exception error) when (DatabaseFailure(error))
        { throw new AiTradeExecutionException(AiTradeExecutionFailure.PersistenceFailure); }
    }

    private static bool DatabaseFailure(Exception error) => error is DbException or DbUpdateException
        || error is InvalidOperationException && error.GetBaseException() is DbException;
}
