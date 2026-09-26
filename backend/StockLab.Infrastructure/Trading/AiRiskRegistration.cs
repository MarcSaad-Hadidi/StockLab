using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Trading;

public static class AiRiskRegistration
{
    public static IServiceCollection AddAiRiskManager(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiRiskOptions>()
            .Bind(configuration.GetSection(AiRiskOptions.SectionName))
            .Validate(options => options.IsValid(), AiRiskOptions.ValidationMessage)
            .ValidateOnStart();
        services.AddScoped<IAiRiskManager, AiRiskManager>();
        return services;
    }
}
