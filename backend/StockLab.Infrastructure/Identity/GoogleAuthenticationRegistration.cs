using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Identity;

public static class GoogleAuthenticationRegistration
{
    public static IServiceCollection AddGoogleAuthentication(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<GoogleAuthOptions>().Bind(configuration.GetSection(GoogleAuthOptions.SectionName))
            .Validate(value => value.IsValid(environment.IsDevelopment()), "GoogleAuth requires a web client ID, secret and exact secure allowed origins.")
            .ValidateOnStart();
        services.AddHttpClient("GoogleOAuth", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.MaxResponseContentBufferSize = 64 * 1024;
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(provider =>
            new ConfigurationManager<OpenIdConnectConfiguration>("https://accounts.google.com/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("GoogleOAuth")) { RequireHttps = true }));
        services.AddSingleton<GoogleIdTokenValidator>();
        services.AddScoped<IGoogleIdentityProvider, GoogleIdentityProvider>();
        services.AddSingleton<GoogleLinkTicketStore>();
        services.AddScoped<IGoogleAuthenticationService, GoogleAuthenticationService>();
        return services;
    }
}
