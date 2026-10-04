using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public static class AiCurrentPositionsRegistration
{
    public static IServiceCollection AddAiCurrentPositions(this IServiceCollection services)
    {
        services.AddScoped<IAiCurrentPositionsService, AiCurrentPositionsService>();
        return services;
    }
}
