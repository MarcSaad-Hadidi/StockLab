namespace StockLab.Application.Interfaces;

public interface IPortfolioPerformanceCaptureService
{
    Task RunOnceAsync(CancellationToken cancellationToken);
}
