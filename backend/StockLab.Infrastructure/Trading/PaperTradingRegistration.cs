using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;

namespace StockLab.Infrastructure.Trading;

public static class PaperTradingRegistration
{
    public static IServiceCollection AddPaperTrading(this IServiceCollection services)
    {
        // Reuse the scoped options configured by AddPersistence without sharing tracked entities.
        services.AddDbContextFactory<StockLabDbContext>(lifetime: ServiceLifetime.Scoped);
        services.AddScoped<IPaperTradingEngine, PaperTradingEngine>();
        return services;
    }
}
