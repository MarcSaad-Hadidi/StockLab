using StockLab.Application.Alerts;

namespace StockLab.Application.Interfaces;

public interface IPriceAlertMonitoringService
{
    /// <summary>Reads active alerts and evaluates at most one quote per symbol within the monitoring budget, without updating alert state.</summary>
    Task<PriceAlertMonitoringResult> RunOnceAsync(CancellationToken cancellationToken = default);
}
