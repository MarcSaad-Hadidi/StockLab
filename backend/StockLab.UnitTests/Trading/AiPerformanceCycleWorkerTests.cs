using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Trading;
using StockLab.UnitTests.Alerts;

namespace StockLab.UnitTests.Trading;

public sealed class AiPerformanceCycleWorkerTests
{
    [Theory]
    [InlineData("00:00:00", false)]
    [InlineData("00:00:59", false)]
    [InlineData("2.00:00:00", false)]
    [InlineData("01:00:00", true)]
    public async Task Invalid_interval_or_enabled_without_target_fails_startup(string interval, bool enabled)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["AiPerformanceCycle:Interval"] = interval, ["AiPerformanceCycle:Enabled"] = enabled.ToString() });
        builder.Services.AddAiPerformanceCycle(builder.Configuration);
        using var host = builder.Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task Disabled_worker_does_not_create_timers_or_resolve_cycle_services()
    {
        var clock = new MonitoringClock();
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var worker = new AiPerformanceCycleWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AiPerformanceCycleOptions()), clock, NullLogger<AiPerformanceCycleWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(clock.TimerCreated.Task.IsCompleted);
    }

    [Fact]
    public async Task Ticks_are_sequential_retry_failure_and_shutdown_cancels_and_disposes_scope()
    {
        var clock = new MonitoringClock();
        var calls = Channel.CreateUnbounded<Probe>();
        var disposed = Channel.CreateUnbounded<Probe>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var registration = new ServiceCollection();
        registration.AddScoped<IAiPerformanceCycleService>(_ => new Probe(calls, disposed,
            Interlocked.Increment(ref count) == 1 ? null : release.Task));
        await using var services = registration.BuildServiceProvider();
        using var worker = new AiPerformanceCycleWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AiPerformanceCycleOptions { Enabled = true, ExpectedDatabase = "test" }), clock,
            NullLogger<AiPerformanceCycleWorker>.Instance);
        await worker.StartAsync(default);
        await clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(calls.Reader.TryRead(out _));
        clock.Advance(TimeSpan.FromHours(1));
        var first = await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first, await disposed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        clock.Advance(TimeSpan.FromHours(1));
        var second = await calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotSame(first, second);
        clock.Advance(TimeSpan.FromHours(4));
        Assert.False(calls.Reader.TryRead(out _));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
        Assert.True(second.Token.IsCancellationRequested);
        Assert.Same(second, await disposed.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    private sealed class Probe(Channel<Probe> calls, Channel<Probe> disposed, Task? gate) : IAiPerformanceCycleService, IAsyncDisposable
    {
        public CancellationToken Token;
        public async Task<AiPerformanceCycleResult> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            calls.Writer.TryWrite(this);
            if (gate is null) throw new InvalidOperationException("private failure");
            await gate.WaitAsync(cancellationToken);
            throw new InvalidOperationException();
        }
        public Task<IReadOnlyList<AiPerformanceCycleObservation>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiPerformanceCycleDetail?> GetByIdAsync(Guid captureId, int offset = 0, int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { disposed.Writer.TryWrite(this); return ValueTask.CompletedTask; }
    }
}
