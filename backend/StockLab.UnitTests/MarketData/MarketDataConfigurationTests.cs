using System.Diagnostics;
using StockLab.Api.Controllers;

namespace StockLab.UnitTests.MarketData;

public sealed class MarketDataConfigurationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Actual_startup_rejects_nonpositive_cache_size(string value)
    {
        // This validation lives in Program.cs, not in MarketDataCacheOptions.
        // Exercise the built entry point instead of copying its validation predicate.
        // Invalid options stop startup before any HTTP listener or provider call.
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(StocksController).Assembly.Location);
        start.ArgumentList.Add($"--MarketDataCache:SizeLimit={value}");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout + await stderr;
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("OptionsValidationException", output);
            Assert.Contains("Cache SizeLimit must be positive.", output);
            Assert.DoesNotContain("Now listening on", output);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
