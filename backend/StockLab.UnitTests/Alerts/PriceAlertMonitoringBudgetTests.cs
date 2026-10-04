using Microsoft.Extensions.Options;
using StockLab.Infrastructure.Alerts;

namespace StockLab.UnitTests.Alerts;

public sealed class PriceAlertMonitoringBudgetTests
{
    [Fact]
    public void Concurrent_reservations_cannot_exceed_the_shared_daily_limit()
    {
        var budget = new PriceAlertMonitoringBudget(Options.Create(new PriceAlertMonitoringOptions { DailyQuoteBudget = 50 }), new MonitoringClock());
        var acquired = 0;

        Parallel.For(0, 1000, _ =>
        {
            if (budget.TryAcquire("AAPL")) Interlocked.Increment(ref acquired);
        });

        Assert.Equal(50, acquired);
        Assert.False(budget.TryAcquire("MSFT"));
    }

    [Fact]
    public void Clock_correction_does_not_reset_the_spent_budget()
    {
        var clock = new MonitoringClock();
        var budget = new PriceAlertMonitoringBudget(Options.Create(new PriceAlertMonitoringOptions { DailyQuoteBudget = 1 }), clock);
        Assert.True(budget.TryAcquire("AAPL"));

        clock.Advance(TimeSpan.FromDays(-1));

        Assert.False(budget.TryAcquire("MSFT"));
        Assert.Equal("AAPL", budget.LastAttemptedSymbol);
    }
}
