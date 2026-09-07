using StockLab.Application.Interfaces;
using StockLab.Infrastructure.MarketData;
using StockLab.Api.Middleware;
using StockLab.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

const string frontendCorsPolicy = "Frontend";

builder.Services.AddControllers().ConfigureApiBehaviorOptions(ApiValidation.Configure);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.AddOptions<MarketDataCacheOptions>()
    .Bind(builder.Configuration.GetSection(MarketDataCacheOptions.SectionName))
    .Validate(options => options.HasValidTtls(), "Cache TTLs must be positive and at most 365 days.")
    .ValidateOnStart();
builder.Services.AddSingleton<MockMarketDataProvider>();
builder.Services.AddSingleton<IMarketDataProvider>(services => new CachingMarketDataProvider(
    services.GetRequiredService<MockMarketDataProvider>(),
    services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
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
