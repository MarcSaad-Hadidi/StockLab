using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace StockLab.Infrastructure.MarketData;

public sealed class TwelveDataOptions
{
    public string ActiveWebsiteKey { get; set; } = "Website";
    public int TimeoutSeconds { get; set; } = 10;
    public bool IsValid() => ActiveWebsiteKey is "Website" or "Fallback" && TimeoutSeconds is > 0 and <= 60;
}

/// <summary>Reads only the selected website credential, never the ML secret or inactive key.</summary>
public sealed class TwelveDataWebsiteCredentialResolver(IConfiguration configuration, IOptions<TwelveDataOptions> options)
{
    public string Resolve()
    {
        var selection = options.Value.ActiveWebsiteKey;
        if (selection is not ("Website" or "Fallback")) throw Invalid();
        var value = configuration[$"TwelveData:Keys:{selection}"];
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
            throw Invalid();
        return value;
    }

    private static OptionsValidationException Invalid() =>
        new("TwelveData", typeof(TwelveDataOptions), ["The selected website credential is missing or invalid."]);
}
