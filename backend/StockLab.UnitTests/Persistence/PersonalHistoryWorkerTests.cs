using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Portfolios;
using StockLab.UnitTests.Alerts;

namespace StockLab.UnitTests.Persistence;

public sealed class PersonalHistoryWorkerTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(3600, true)]
    [InlineData(86400, true)]
    [InlineData(86401, false)]
    public void Capture_interval_is_bounded_and_defaults_to_one_hour(int seconds, bool valid)
    {
        Assert.Equal(TimeSpan.FromHours(1), new PortfolioPerformanceCaptureOptions().Interval);
        Assert.True(new PortfolioPerformanceCaptureOptions().Enabled);
        Assert.Equal(valid, new PortfolioPerformanceCaptureOptions { Interval = TimeSpan.FromSeconds(seconds) }.IsValid());
    }

    [Fact]
    public async Task Disabled_capture_does_not_create_a_timer_or_resolve_services()
    {
        var clock = new MonitoringClock();
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var worker = new PortfolioPerformanceCaptureWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PortfolioPerformanceCaptureOptions { Enabled = false }), clock,
            NullLogger<PortfolioPerformanceCaptureWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(clock.TimerCreated.Task.IsCompleted);
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task Capture_waits_for_the_interval_recovers_from_errors_and_cancels_active_work_on_shutdown()
    {
        var clock = new MonitoringClock();
        var probe = new Probe();
        await using var services = new ServiceCollection()
            .AddScoped<IPortfolioPerformanceCaptureService>(_ => new Capture(probe)).BuildServiceProvider();
        using var worker = new PortfolioPerformanceCaptureWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PortfolioPerformanceCaptureOptions { Interval = TimeSpan.FromMinutes(1) }), clock,
            NullLogger<PortfolioPerformanceCaptureWorker>.Instance);
        await worker.StartAsync(default);
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(probe.Runs.Reader.TryRead(out _));
        clock.Advance(TimeSpan.FromSeconds(1));
        var first = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(first.Id, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(first.Id, second.Id);
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(second.Token.IsCancellationRequested);
        Assert.Equal(second.Id, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    private sealed class Probe
    {
        public int Created;
        public Channel<(int Id, CancellationToken Token)> Runs { get; } = Channel.CreateUnbounded<(int, CancellationToken)>();
        public Channel<int> Disposed { get; } = Channel.CreateUnbounded<int>();
    }
    private sealed class Capture(Probe probe) : IPortfolioPerformanceCaptureService, IAsyncDisposable
    {
        private readonly int id = Interlocked.Increment(ref probe.Created);
        public async Task RunOnceAsync(CancellationToken token)
        {
            probe.Runs.Writer.TryWrite((id, token));
            if (id == 1) throw new InvalidOperationException("test-only provider failure");
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        public ValueTask DisposeAsync() { probe.Disposed.Writer.TryWrite(id); return ValueTask.CompletedTask; }
    }
}
