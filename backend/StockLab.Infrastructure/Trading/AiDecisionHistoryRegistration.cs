using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public static class AiDecisionHistoryRegistration
{
    public static IServiceCollection AddAiDecisionHistory(this IServiceCollection services)
    {
        services.AddDbContextFactory<StockLabDbContext>(lifetime: ServiceLifetime.Scoped);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAiDecisionHistoryService, AiDecisionHistoryService>();
        return services;
    }
}
