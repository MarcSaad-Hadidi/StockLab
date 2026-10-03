using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;

namespace StockLab.Infrastructure.Trading;

public static class AiTradeExecutionRegistration
{
    public static IServiceCollection AddAiTradeExecution(this IServiceCollection services)
    {
        // Reuse rate limiting and in-flight deduplication, without the website's completed-quote cache.
        services.AddKeyedSingleton<IMarketDataProvider>("Execution", (provider, _) =>
            provider.GetRequiredService<DeduplicatingMarketDataProvider>());
        services.AddScoped<IAiTradeExecutionService, AiPaperTradingEngine>();
        return services;
    }
}
