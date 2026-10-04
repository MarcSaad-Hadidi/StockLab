using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Alerts;

public static class PriceAlertMonitoringRegistration
{
    public static IServiceCollection AddPriceAlertMonitoring(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PriceAlertMonitoringOptions>()
            .Bind(configuration.GetSection(PriceAlertMonitoringOptions.SectionName))
            .Validate(options => options.IsValid(), PriceAlertMonitoringOptions.ValidationMessage)
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IPriceAlertMonitoringService, PriceAlertMonitoringService>();
        services.AddHostedService<PriceAlertMonitoringWorker>();
        return services;
    }
}
