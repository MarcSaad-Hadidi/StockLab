using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Portfolios;

public static class PortfolioRegistration
{
    public static IServiceCollection AddPortfolio(this IServiceCollection services)
    {
        services.AddScoped<IPortfolioService, PortfolioService>();
        return services;
    }
}
