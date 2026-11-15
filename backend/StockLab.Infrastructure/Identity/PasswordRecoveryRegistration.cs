using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Identity;

public static class PasswordRecoveryRegistration
{
    public static IServiceCollection AddPasswordRecovery(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<PasswordRecoveryOptions>().Bind(configuration.GetSection(PasswordRecoveryOptions.SectionName))
            .Validate(options => options.IsValid(environment.IsDevelopment()), "Password recovery configuration is invalid.")
            .ValidateOnStart();
        services.AddSingleton<PasswordRecoveryRequestQueue>();
        services.AddSingleton<IPasswordRecoveryRequestQueue>(provider => provider.GetRequiredService<PasswordRecoveryRequestQueue>());
        services.AddScoped<IUserPasswordRecoveryService, UserPasswordRecoveryService>();
        services.AddScoped<PasswordRecoveryRequestProcessor>();
        services.AddSingleton<IPasswordRecoveryEmailSender, SmtpPasswordRecoveryEmailSender>();
        services.AddHostedService<PasswordRecoveryWorker>();
        return services;
    }
}
