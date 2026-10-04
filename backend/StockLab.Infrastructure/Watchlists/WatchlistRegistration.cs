using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Watchlists;

public static class WatchlistRegistration
{
    public static IServiceCollection AddWatchlist(this IServiceCollection services)
    {
        services.AddScoped<IWatchlistService, WatchlistService>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
