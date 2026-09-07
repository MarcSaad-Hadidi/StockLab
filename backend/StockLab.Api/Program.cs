using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;
using StockLab.Api.Middleware;
using StockLab.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

const string frontendCorsPolicy = "Frontend";

builder.Services.AddControllers().ConfigureApiBehaviorOptions(ApiValidation.Configure);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
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
builder.Services.AddSingleton<MockMarketDataProvider>();
builder.Services.AddSingleton<DeduplicatingMarketDataProvider>(services =>
    new DeduplicatingMarketDataProvider(services.GetRequiredService<MockMarketDataProvider>()));
builder.Services.AddSingleton<IMarketDataProvider>(services => new CachingMarketDataProvider(
    services.GetRequiredService<DeduplicatingMarketDataProvider>(),
    services.GetRequiredKeyedService<Microsoft.Extensions.Caching.Memory.IMemoryCache>("MarketData"),
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataCacheOptions>>()));
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

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(frontendCorsPolicy);

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
