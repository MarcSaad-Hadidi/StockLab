using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Alerts;

public static class PriceAlertRegistration
{
    public static IServiceCollection AddPriceAlerts(this IServiceCollection services)
    {
        services.AddScoped<IPriceAlertService, PriceAlertService>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
