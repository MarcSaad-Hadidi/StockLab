using StockLab.Application.DTOs.MarketData;

namespace StockLab.Application.Trading;

public static class TradingSymbol
{
    public static string? FromQuote(StockQuote quote)
    {
        var ticker = quote.Symbol.Trim().ToUpperInvariant().Split(':', 2)[0];
        var exchange = quote.Exchange?.Trim().ToUpperInvariant();
        if (ticker.Length == 0 || string.IsNullOrWhiteSpace(exchange) || exchange.Contains(':'))
            return null;

        var canonical = $"{ticker}:{exchange}";
        return canonical.Length <= 32 ? canonical : null;
    }
}
