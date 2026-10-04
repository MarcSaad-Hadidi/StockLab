using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockLab.Application.Alerts;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Alerts;

public sealed class PriceAlertMonitoringService(
    StockLabDbContext dbContext,
    IMarketDataProvider marketDataProvider,
    PriceAlertMonitoringBudget budget,
    ILogger<PriceAlertMonitoringService> logger) : IPriceAlertMonitoringService
{
    public async Task<PriceAlertMonitoringResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var alerts = await dbContext.PriceAlerts.AsNoTracking()
            .Where(alert => alert.Status == "Active")
            .Select(alert => new
            {
                alert.Id, alert.UserId, alert.Symbol, alert.Currency, alert.Condition, alert.TargetPrice
            })
            .ToArrayAsync(cancellationToken);
        var groups = alerts.GroupBy(alert => alert.Symbol, StringComparer.Ordinal).ToArray();
        var matches = new List<PriceAlertMatch>();
        var quoteCount = 0;
        var failedQuoteCount = 0;
        var deferredSymbolCount = 0;
        var lastAttemptedSymbol = budget.LastAttemptedSymbol;

        // Resume after the last attempted symbol so a partial cycle does not starve later symbols.
        foreach (var group in groups.OrderBy(group => string.CompareOrdinal(group.Key, lastAttemptedSymbol) <= 0 ? 1 : 0)
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.TryAcquire(group.Key))
            {
                deferredSymbolCount = groups.Length - quoteCount - failedQuoteCount;
                break;
            }
            var logSymbol = string.Concat(group.Key.Select(character =>
                char.IsControl(character) || character is '\u2028' or '\u2029' ? '?' : character));
            StockQuote? quote;
            try
            {
                quote = await marketDataProvider.GetQuoteAsync(group.Key, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failedQuoteCount++;
                // Upstream exceptions may contain credentials or raw response data.
                logger.LogWarning("Price alert quote lookup failed for {Symbol}; continuing the cycle.", logSymbol);
                continue;
            }

            if (quote is null)
            {
                failedQuoteCount++;
                logger.LogWarning("Price alert quote unavailable for {Symbol}; skipping the symbol.", logSymbol);
                continue;
            }

            quoteCount++;
            var currencyMismatchCount = 0;
            foreach (var alert in group)
            {
                if (!string.Equals(alert.Currency, quote.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    currencyMismatchCount++;
                    continue;
                }

                var conditionReached = alert.Condition switch
                {
                    "Above" => quote.Price > alert.TargetPrice,
                    "Below" => quote.Price < alert.TargetPrice,
                    _ => false
                };
                if (conditionReached)
                {
                    matches.Add(new PriceAlertMatch(alert.Id, alert.UserId, alert.Symbol, alert.Currency,
                        alert.Condition, alert.TargetPrice, quote.Price, quote.AsOfUtc));
                }
            }

            if (currencyMismatchCount > 0)
            {
                logger.LogWarning("Price alert currency mismatch for {Symbol}; skipped {AlertCount} alerts.",
                    logSymbol, currencyMismatchCount);
            }
        }

        var result = new PriceAlertMonitoringResult(alerts.Length, groups.Length, quoteCount, failedQuoteCount, matches.AsReadOnly(), deferredSymbolCount);
        logger.LogInformation(
            "Price alert monitoring: {ActiveAlertCount} active alerts, {DistinctSymbolCount} distinct symbols, {QuoteCount} quotes retrieved, {MatchCount} matches, {FailedQuoteCount} failures, {DeferredSymbolCount} symbols deferred by the daily budget.",
            result.ActiveAlertCount, result.DistinctSymbolCount, result.QuoteCount, result.Matches.Count, result.FailedQuoteCount, result.DeferredSymbolCount);
        return result;
    }
}
