namespace StockLab.Application.Exceptions;

/// <summary>The local provider-call budget cannot accept another request.</summary>
public sealed class MarketDataRateLimitException : Exception
{
    public MarketDataRateLimitException() : base("Market data requests are temporarily rate limited.") { }
}
