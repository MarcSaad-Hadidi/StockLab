using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public static class AiTradesHistoryRegistration
{
    public static IServiceCollection AddAiTradesHistory(this IServiceCollection services)
    {
        services.AddDbContextFactory<StockLabDbContext>(lifetime: ServiceLifetime.Scoped);
        services.AddScoped<IAiTradesHistoryService, AiTradesHistoryService>();
        return services;
    }
}
