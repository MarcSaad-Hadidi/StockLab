using Microsoft.Extensions.DependencyInjection;
using StockLab.Api.Operations;
using StockLab.Application.Interfaces;
using StockLab.UnitTests.Trading;

namespace StockLab.UnitTests.Api;

public sealed class AiPerformanceCycleCommandTests
{
    [Fact]
    public async Task Command_captures_and_replays_without_quotes_or_unrelated_mutations()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        await f.InitializeAsync();
        await f.ExecuteAsync();
        var registration = new ServiceCollection();
        registration.AddScoped<IAiPerformanceCycleService>(_ => AiPerformanceCycleTests.Service(f));
        await using var services = registration.BuildServiceProvider();
        var output = new StringWriter();
        Assert.Equal(0, await AiPerformanceCycleCommand.RunAsync(services, output));
        Assert.Contains("replay=False", output.ToString());
        var before = await f.StateAsync();
        f.ValuationMarket.Quote = (_, _) => throw new InvalidOperationException("PRIVATE_PROVIDER");
        output = new StringWriter();
        Assert.Equal(0, await AiPerformanceCycleCommand.RunAsync(services, output));
        Assert.Contains("replay=True", output.ToString());
        Assert.Equal(before, await f.StateAsync());
    }

    [Fact]
    public async Task Failures_and_cancellation_return_safe_nonzero_output()
    {
        await using var f = await AiCurrentPositionsFixture.CreateAsync();
        var registration = new ServiceCollection();
        registration.AddScoped<IAiPerformanceCycleService>(_ => AiPerformanceCycleTests.Service(f, target: "PRIVATE_DATABASE"));
        await using var services = registration.BuildServiceProvider();
        var output = new StringWriter();
        Assert.Equal(1, await AiPerformanceCycleCommand.RunAsync(services, output));
        Assert.DoesNotContain("PRIVATE_DATABASE", output.ToString());
        Assert.Contains("performance_cycle_failed", output.ToString());
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        output = new StringWriter();
        Assert.Equal(1, await AiPerformanceCycleCommand.RunAsync(services, output, cancel.Token));
        Assert.Contains("performance_cycle_cancelled", output.ToString());
    }
}
