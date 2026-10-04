using Microsoft.EntityFrameworkCore;
using StockLab.Application.Alerts;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Alerts;

public sealed class PriceAlertService(StockLabDbContext dbContext, TimeProvider timeProvider) : IPriceAlertService
{
    public async Task<IReadOnlyList<PriceAlert>> GetAllAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.PriceAlerts.AsNoTracking()
            .Where(alert => alert.UserId == userId)
            .OrderByDescending(alert => alert.CreatedAtUtc)
            .ThenBy(alert => alert.Id)
            .ToArrayAsync(cancellationToken);

    public async Task<PriceAlert> CreateAsync(Guid userId, string symbol, string condition, decimal targetPrice, CancellationToken cancellationToken)
    {
        var alert = new PriceAlert
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Symbol = PriceAlertValidation.NormalizeSymbol(symbol),
            Currency = "USD",
            Condition = PriceAlertValidation.NormalizeCondition(condition),
            TargetPrice = PriceAlertValidation.NormalizeTargetPrice(targetPrice),
            Status = "Active",
            TriggeredPrice = null,
            TriggeredAtUtc = null
        };
        var now = timeProvider.GetUtcNow().UtcDateTime;
        alert.CreatedAtUtc = now;
        alert.UpdatedAtUtc = now;
        dbContext.PriceAlerts.Add(alert);
        await dbContext.SaveChangesAsync(cancellationToken);
        return alert;
    }

    public async Task<PriceAlert?> UpdateAsync(Guid userId, Guid alertId, string condition, decimal targetPrice, CancellationToken cancellationToken)
    {
        var normalizedCondition = PriceAlertValidation.NormalizeCondition(condition);
        var normalizedPrice = PriceAlertValidation.NormalizeTargetPrice(targetPrice);
        var alert = await FindOwnedAsync(userId, alertId, cancellationToken);
        if (alert is null) return null;
        EnsureMutable(alert);
        alert.Condition = normalizedCondition;
        alert.TargetPrice = normalizedPrice;
        alert.UpdatedAtUtc = UpdateTime(alert);
        await SaveMutationAsync(alert, cancellationToken);
        return alert;
    }

    public async Task<PriceAlert?> DisableAsync(Guid userId, Guid alertId, CancellationToken cancellationToken)
    {
        var alert = await FindOwnedAsync(userId, alertId, cancellationToken);
        if (alert is null) return null;
        EnsureMutable(alert);
        if (alert.Status == "Disabled") return alert;
        alert.Status = "Disabled";
        alert.UpdatedAtUtc = UpdateTime(alert);
        await SaveMutationAsync(alert, cancellationToken);
        return alert;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid alertId, CancellationToken cancellationToken)
    {
        var alert = await FindOwnedAsync(userId, alertId, cancellationToken);
        if (alert is null) return false;
        dbContext.PriceAlerts.Remove(alert);
        await SaveMutationAsync(alert, cancellationToken);
        return true;
    }

    private Task<PriceAlert?> FindOwnedAsync(Guid userId, Guid alertId, CancellationToken cancellationToken) =>
        dbContext.PriceAlerts.SingleOrDefaultAsync(alert => alert.Id == alertId && alert.UserId == userId, cancellationToken);

    private static void EnsureMutable(PriceAlert alert)
    {
        if (alert.Status == "Triggered") throw new PriceAlertAlreadyTriggeredException();
    }

    private DateTime UpdateTime(PriceAlert alert)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Clock corrections must not violate the persisted timestamp invariant.
        return now < alert.UpdatedAtUtc ? alert.UpdatedAtUtc : now;
    }

    private async Task SaveMutationAsync(PriceAlert alert, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Entry(alert).State = EntityState.Detached;
            throw new PriceAlertUpdateConflictException();
        }
    }
}
