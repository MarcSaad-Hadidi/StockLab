using System.Text;
using System.Threading.RateLimiting;
using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;
using StockLab.Api.Middleware;
using StockLab.Api.Validation;
using StockLab.Api.Configuration;
using StockLab.Api.DTOs;

using StockLab.Infrastructure.MarketEnrichment;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Identity;
using StockLab.Infrastructure.Trading;
using StockLab.Infrastructure.Portfolios;

var builder = WebApplication.CreateBuilder(args);

const string frontendCorsPolicy = "Frontend";

builder.Services.AddControllers().ConfigureApiBehaviorOptions(ApiValidation.Configure);
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT"
            }
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
        });
        return Task.CompletedTask;
    });
});
builder.Services.AddHealthChecks();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddUserRegistration();
builder.Services.AddPaperTrading();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
builder.Services.AddAiTraderPortfolio();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(options => options.IsValid(), JwtOptions.ValidationMessage)
    .ValidateOnStart();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<JwtOptions>>((options, jwtOptionsAccessor) =>
    {
        var jwtConfiguration = jwtOptionsAccessor.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtConfiguration.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtConfiguration.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = string.IsNullOrWhiteSpace(jwtConfiguration.SigningKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfiguration.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new ApiErrorResponse("unauthorized", "Authentication is required."),
                    context.HttpContext.RequestAborted);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IConfiguration>((options, configuration) =>
    {
        var trustedForwardedHeaderProxies = configuration
            .GetSection("ForwardedHeaders:KnownProxies")
            .GetChildren()
            .Select(proxyConfiguration => proxyConfiguration.Value)
            .Where(proxyAddress => !string.IsNullOrWhiteSpace(proxyAddress))
            .Select(proxyAddress => proxyAddress!)
            .ToArray();
        var parsedTrustedForwardedHeaderProxies = new List<IPAddress>(trustedForwardedHeaderProxies.Length);
        foreach (var proxyAddress in trustedForwardedHeaderProxies)
        {
            if (!IPAddress.TryParse(proxyAddress, out var parsedProxyAddress))
            {
                throw new InvalidOperationException("ForwardedHeaders:KnownProxies must contain valid IP addresses.");
            }

            parsedTrustedForwardedHeaderProxies.Add(parsedProxyAddress);
        }

        options.ForwardedHeaders = parsedTrustedForwardedHeaderProxies.Count > 0
            ? ForwardedHeaders.XForwardedFor
            : ForwardedHeaders.None;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxyAddress in parsedTrustedForwardedHeaderProxies)
        {
            options.KnownProxies.Add(proxyAddress);
        }
    });
builder.Services.AddOptions<LoginRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(LoginRateLimitOptions.SectionName))
    .Validate(options => options.IsValid(), LoginRateLimitOptions.ValidationMessage)
    .ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse("too_many_requests", "Too many login attempts. Try again later."),
            cancellationToken);
    };
    options.AddPolicy(LoginRateLimitOptions.PolicyName, httpContext =>
    {
        var remoteAddress = httpContext.Connection.RemoteIpAddress;
        var partitionKey = remoteAddress is null
            ? "unknown"
            : remoteAddress.IsIPv4MappedToIPv6
                ? remoteAddress.MapToIPv4().ToString()
                : remoteAddress.ToString();
        var loginRateLimitOptions = httpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<LoginRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => loginRateLimitOptions.CreateLimiterOptions());
    });
});
builder.Services.AddOptions<MarketDataCacheOptions>()
    .Bind(builder.Configuration.GetSection(MarketDataCacheOptions.SectionName))
    .Validate(options => options.HasValidTtls(), "Cache TTLs must be positive and at most 365 days.")
    .Validate(options => options.SizeLimit > 0, "Cache SizeLimit must be positive.")
    .ValidateOnStart();
// A dedicated cache avoids imposing size accounting on unrelated application caches.
builder.Services.AddKeyedSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>("MarketData", (services, _) =>
    new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions
    {
        SizeLimit = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataCacheOptions>>().Value.SizeLimit
    }));
builder.Services.AddMarketDataTerminal(builder.Configuration);
builder.Services.AddOptions<MarketDataRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(MarketDataRateLimitOptions.SectionName))
    .Validate(options => options.IsValid(), MarketDataRateLimitOptions.ValidationMessage)
    .ValidateOnStart();
builder.Services.AddSingleton<System.Threading.RateLimiting.RateLimiter>(services =>
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataRateLimitOptions>>().Value.CreateLimiter());
builder.Services.AddSingleton<RateLimitedMarketDataProvider>(services => new RateLimitedMarketDataProvider(
    services.GetRequiredKeyedService<IMarketDataProvider>("Terminal"),
    services.GetRequiredService<System.Threading.RateLimiting.RateLimiter>(),
    services.GetRequiredService<ILogger<RateLimitedMarketDataProvider>>()));
builder.Services.AddSingleton<DeduplicatingMarketDataProvider>(services =>
    new DeduplicatingMarketDataProvider(services.GetRequiredService<RateLimitedMarketDataProvider>()));
builder.Services.AddSingleton<IMarketDataProvider>(services => new CachingMarketDataProvider(
    services.GetRequiredService<DeduplicatingMarketDataProvider>(),
    services.GetRequiredKeyedService<Microsoft.Extensions.Caching.Memory.IMemoryCache>("MarketData"),
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataCacheOptions>>()));
builder.Services.AddOptions<AlphaVantageOptions>().Bind(builder.Configuration.GetSection("AlphaVantage"))
    .Validate(o => o.IsValid(), "Invalid Alpha Vantage budget, timeout or cache TTL.").ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient("AlphaVantage", client =>
{
    client.BaseAddress = new Uri("https://www.alphavantage.co/");
    client.Timeout = Timeout.InfiniteTimeSpan; // Provider owns a bounded timeout, including body reads.
    client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
}).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IMarketEnrichmentProvider, AlphaVantageProvider>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendCorsPolicy, policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(frontendCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
