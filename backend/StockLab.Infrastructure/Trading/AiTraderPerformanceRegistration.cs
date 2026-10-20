using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public static class AiTraderPerformanceRegistration
{
    public static IServiceCollection AddAiTraderPerformance(this IServiceCollection services)
    {
        services.AddScoped<IAiTraderPerformanceService, AiTraderPerformanceService>();
        services.AddScoped<IAiTraderValuationHistoryService, AiTraderValuationHistoryService>();
        return services;
    }
}
