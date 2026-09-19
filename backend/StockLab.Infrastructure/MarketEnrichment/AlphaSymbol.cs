using System.Text.RegularExpressions;
namespace StockLab.Infrastructure.MarketEnrichment;
public static partial class AlphaSymbol
{
    public static string Canonical(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var normalized = symbol.Trim().ToUpperInvariant();
        if (!SymbolPattern().IsMatch(normalized)) throw new ArgumentException("Invalid stock symbol.");
        return normalized;
    }
    public static string Resolve(string symbol)
    {
        var canonical = Canonical(symbol);
        var parts = canonical.Split(':');
        if (parts.Length == 1) return canonical; // Preserve international dot suffixes.
        if (parts[1] is "NASDAQ" or "NYSE" or "NYSEAMERICAN" or "AMEX") return parts[0];
        throw new NotSupportedException("This exchange-qualified symbol is not supported for enrichment.");
    }
    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9.\-]{0,29}(:[A-Z0-9]{1,16})?$")]
    private static partial Regex SymbolPattern();
}
