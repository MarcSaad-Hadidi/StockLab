using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiRiskRegistrationTests
{
    [Theory]
    [InlineData("MinimumConfidence", "-0.1")]
    [InlineData("MinimumConfidence", "1.1")]
    [InlineData("MaxPositionExposurePercent", "0")]
    [InlineData("MaxPositionExposurePercent", "1.1")]
    [InlineData("MaxOpenPositions", "0")]
    [InlineData("MaxCashAllocationPerTradePercent", "0")]
    [InlineData("MaxCashAllocationPerTradePercent", "1.1")]
    [InlineData("MaxCashAllocationPerTradePercent", "0.3")]
    [InlineData("AllowShortSelling", "true")]
    public async Task Invalid_policy_fails_at_host_start_without_resolving_manager(string property, string value)
    {
        using var host = new HostBuilder().ConfigureServices(services => services.AddAiRiskManager(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AiRiskOptions.SectionName}:{property}"] = value
            }).Build())).Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Valid_confidence_boundaries_and_defaults_start_successfully(string confidence)
    {
        using var host = new HostBuilder().ConfigureServices(services => services.AddAiRiskManager(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AiRiskOptions.SectionName}:MinimumConfidence"] = confidence
            }).Build())).Build();
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<AiRiskOptions>>().Value;
        Assert.Equal(0.2m, options.MaxPositionExposurePercent);
        Assert.Equal(0.1m, options.MaxCashAllocationPerTradePercent);
        Assert.Equal(10, options.MaxOpenPositions);
        Assert.False(options.AllowShortSelling);
        await host.StopAsync();
    }

    [Fact]
    public void Manager_has_only_portfolio_and_policy_dependencies()
    {
        var parameters = Assert.Single(typeof(AiRiskManager).GetConstructors()).GetParameters();
        Assert.Equal([typeof(IAiTraderPortfolioService), typeof(IOptions<AiRiskOptions>)], parameters.Select(p => p.ParameterType));
    }
}
