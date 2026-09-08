namespace StockLab.Application.Exceptions;
public enum MarketEnrichmentFailure { LocalBudgetExceeded, QuotaExceeded, RateLimited, ProviderUnavailable, MalformedResponse, PermissionOrEntitlement, Timeout }
public sealed class MarketEnrichmentException(MarketEnrichmentFailure category) : Exception("Market enrichment is temporarily unavailable.")
{
    public MarketEnrichmentFailure Category { get; } = category;
}
