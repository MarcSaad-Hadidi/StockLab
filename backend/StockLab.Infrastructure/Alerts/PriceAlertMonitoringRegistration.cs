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
            .Validate(options => options.DailyQuoteBudget > 0, "PriceAlertMonitoring:DailyQuoteBudget must be positive.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PriceAlertMonitoringBudget>();
        services.AddScoped<IPriceAlertMonitoringService, PriceAlertMonitoringService>();
        services.AddHostedService<PriceAlertMonitoringWorker>();
        return services;
    }
}
