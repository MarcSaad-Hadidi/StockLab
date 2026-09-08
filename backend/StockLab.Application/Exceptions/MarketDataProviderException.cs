namespace StockLab.Application.Exceptions;

public enum MarketDataProviderFailure
{
    InvalidRequest, AuthenticationFailed, PermissionDenied, DataUnavailable,
    UpstreamRateLimited, ProviderUnavailable, Timeout, MalformedResponse, RangeTooLarge
}

/// <summary>Controlled provider failure. Never retains raw messages, bodies or inner exceptions.</summary>
public sealed class MarketDataProviderException(MarketDataProviderFailure category)
    : Exception("Market data provider request failed.")
{
    public MarketDataProviderFailure Category { get; } = category;
}
