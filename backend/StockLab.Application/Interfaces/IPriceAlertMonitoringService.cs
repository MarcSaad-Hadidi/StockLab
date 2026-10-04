using StockLab.Application.Alerts;

namespace StockLab.Application.Interfaces;

public interface IPriceAlertMonitoringService
{
    /// <summary>Reads active alerts and evaluates one quote per symbol, without updating alert state.</summary>
    Task<PriceAlertMonitoringResult> RunOnceAsync(CancellationToken cancellationToken = default);
}
