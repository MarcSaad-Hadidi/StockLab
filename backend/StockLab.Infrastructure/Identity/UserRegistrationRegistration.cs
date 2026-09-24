using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Identity;

public static class UserRegistrationRegistration
{
    public static IServiceCollection AddUserRegistration(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        services.AddScoped<IUserLoginService, UserLoginService>();
        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
        return services;
    }
}
