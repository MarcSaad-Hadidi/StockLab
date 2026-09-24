using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public static class PaperTradingRegistration
{
    public static IServiceCollection AddPaperTrading(this IServiceCollection services)
    {
        services.AddScoped<IPaperTradingEngine, PaperTradingEngine>();
        return services;
    }
}
