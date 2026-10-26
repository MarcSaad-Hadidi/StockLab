using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public static class AiRejectedDecisionHistoryRegistration
{
    public static IServiceCollection AddAiRejectedDecisionHistory(this IServiceCollection services)
    {
        services.AddDbContextFactory<StockLabDbContext>(lifetime: ServiceLifetime.Scoped);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AiRejectedDecisionHistoryService>();
        services.AddScoped<IAiRejectedDecisionHistoryService>(provider => provider.GetRequiredService<AiRejectedDecisionHistoryService>());
        return services;
    }
}
