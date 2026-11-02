using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Portfolios;

public static class PortfolioPerformanceHistoryRegistration
{
    public static IServiceCollection AddPersonalPortfolioHistory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortfolioPerformanceCaptureOptions>()
            .Bind(configuration.GetSection(PortfolioPerformanceCaptureOptions.SectionName))
            .Validate(value => value.IsValid(), "Personal valuation capture interval must be between one minute and one day.")
            .ValidateOnStart();
        services.AddScoped<IPortfolioPerformanceHistoryService, PortfolioPerformanceHistoryService>();
        services.AddScoped<PortfolioPerformanceCaptureService>();
        services.AddScoped<IPortfolioPerformanceCaptureService>(provider => provider.GetRequiredService<PortfolioPerformanceCaptureService>());
        services.AddHostedService<PortfolioPerformanceCaptureWorker>();
        return services;
    }
}
