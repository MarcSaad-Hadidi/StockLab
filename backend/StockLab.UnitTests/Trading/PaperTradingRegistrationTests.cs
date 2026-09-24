using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class PaperTradingRegistrationTests
{
    [Fact]
    public async Task Factory_uses_configured_database_without_reusing_the_scoped_context()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:StockLab"] =
                    "Server=localhost;Database=StockLabTestOnly;Integrated Security=true;TrustServerCertificate=true"
            }).Build();
        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddPaperTrading();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var callerContext = scope.ServiceProvider.GetRequiredService<StockLabDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StockLabDbContext>>();

        await using var firstContext = await factory.CreateDbContextAsync();
        await using var secondContext = await factory.CreateDbContextAsync();

        Assert.NotSame(callerContext, firstContext);
        Assert.NotSame(callerContext, secondContext);
        Assert.NotSame(firstContext, secondContext);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", firstContext.Database.ProviderName);
        Assert.Equal("StockLabTestOnly", firstContext.Database.GetDbConnection().Database);
        Assert.IsType<PaperTradingEngine>(scope.ServiceProvider.GetRequiredService<IPaperTradingEngine>());
        Assert.Same(callerContext, scope.ServiceProvider.GetRequiredService<StockLabDbContext>());
    }
}
