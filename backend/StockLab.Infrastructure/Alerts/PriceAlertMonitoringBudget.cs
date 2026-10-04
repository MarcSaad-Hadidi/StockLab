using Microsoft.Extensions.Options;

namespace StockLab.Infrastructure.Alerts;

public sealed class PriceAlertMonitoringBudget(IOptions<PriceAlertMonitoringOptions> options, TimeProvider timeProvider)
{
    private readonly object gate = new();
    private readonly int dailyQuoteBudget = options.Value.DailyQuoteBudget;
    private DateOnly budgetDay = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    private int used;
    private string? lastAttemptedSymbol;

    public string? LastAttemptedSymbol
    {
        get { lock (gate) return lastAttemptedSymbol; }
    }

    public bool TryAcquire(string symbol)
    {
        lock (gate)
        {
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            if (today > budgetDay)
            {
                budgetDay = today;
                used = 0;
            }
            if (used >= dailyQuoteBudget) return false;

            // Count every attempted facade lookup, including cached, missing and failed quotes.
            // A failure may already have consumed upstream credits; reservations are not refunded.
            used++;
            lastAttemptedSymbol = symbol;
            return true;
        }
    }
}
