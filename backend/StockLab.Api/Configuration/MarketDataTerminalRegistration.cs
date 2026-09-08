using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.Api.Configuration;

public sealed class MarketDataProviderOptions
{
    public string Provider { get; set; } = "Mock";
}

public static class MarketDataTerminalRegistration
{
    public static IServiceCollection AddMarketDataTerminal(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MarketDataProviderOptions>().Bind(configuration.GetSection("MarketData"))
            .Validate(o => o.Provider is "Mock" or "TwelveData", "Provider must be Mock or TwelveData.").ValidateOnStart();
        // Bind only non-secret settings. ML and inactive credentials never enter the options object.
        services.AddOptions<TwelveDataOptions>().Configure(o =>
        {
            o.ActiveWebsiteKey = configuration["TwelveData:ActiveWebsiteKey"] ?? "Website";
            o.TimeoutSeconds = configuration.GetValue("TwelveData:TimeoutSeconds", 10);
        }).Validate(o => o.IsValid(), "ActiveWebsiteKey must be Website or Fallback; TimeoutSeconds must be 1..60.")
            .ValidateOnStart();
        services.AddSingleton<TwelveDataWebsiteCredentialResolver>();
        services.AddOptions<MarketDataProviderOptions>()
            .Validate<TwelveDataWebsiteCredentialResolver>((o, resolver) =>
            {
                if (o.Provider == "TwelveData") resolver.Resolve();
                return true;
            }).ValidateOnStart();

        services.AddSingleton<MockMarketDataProvider>();
        services.AddHttpClient(TwelveDataProvider.ClientName, (sp, client) =>
        {
            client.BaseAddress = new Uri("https://api.twelvedata.com/");
            client.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<TwelveDataOptions>>().Value.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
        .RemoveAllLoggers(); // No HTTP headers, URLs or raw transport failures in factory logs.
        services.AddSingleton<TwelveDataProvider>();
        services.AddKeyedSingleton<IMarketDataProvider>("Terminal", (sp, _) =>
            sp.GetRequiredService<IOptions<MarketDataProviderOptions>>().Value.Provider == "TwelveData"
                ? sp.GetRequiredService<TwelveDataProvider>() : sp.GetRequiredService<MockMarketDataProvider>());
        return services;
    }
}
