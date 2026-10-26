using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public static class AiTraderPerformanceRegistration
{
    public static IServiceCollection AddAiTraderPerformance(this IServiceCollection services)
    {
        services.AddScoped<IAiTraderPerformanceService, AiTraderPerformanceService>();
        services.AddScoped<AiTraderValuationHistoryService>();
        services.AddScoped<IAiTraderValuationHistoryService>(provider => provider.GetRequiredService<AiTraderValuationHistoryService>());
        return services;
    }

    public static IServiceCollection AddAiPerformanceCycle(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiPerformanceCycleOptions>().Bind(configuration.GetSection(AiPerformanceCycleOptions.SectionName))
            .Validate(value => value.IsValid(), "AI performance interval must be 1 minute..1 day; enabled cycles require ExpectedDatabase.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAiPerformanceCycleService, AiPerformanceCycleService>();
        services.AddHostedService<AiPerformanceCycleWorker>();
        return services;
    }
}
