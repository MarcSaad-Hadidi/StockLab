using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockLab.Application.Alerts;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Alerts;

namespace StockLab.UnitTests.Alerts;

public sealed class PriceAlertMonitoringWorkerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Waits_for_configured_interval_and_disposes_a_fresh_scope_per_cycle()
    {
        var probe = new CycleProbe();
        var clock = new MonitoringClock();
        var configuredInterval = TimeSpan.FromSeconds(17);
        await using var services = Services(probe);
        using var worker = Worker(services, clock, configuredInterval);
        await worker.StartAsync(default);
        try
        {
            await clock.TimerCreated.Task.WaitAsync(TestTimeout);
            clock.Advance(TimeSpan.FromSeconds(16));
            Assert.False(probe.Runs.Reader.TryRead(out _));
            clock.Advance(TimeSpan.FromSeconds(1));
            var first = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
            Assert.Equal(first.ScopeId, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
            clock.Advance(configuredInterval);
            var second = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
            Assert.Equal(second.ScopeId, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
            Assert.NotEqual(first.ScopeId, second.ScopeId);
            Assert.Equal(1, probe.MaxActive);
        }
        finally { await StopAsync(worker); }
    }

    [Fact]
    public async Task Slow_cycle_never_overlaps_later_ticks()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new CycleProbe { Handler = (_, ct) => release.Task.WaitAsync(ct) };
        var clock = new MonitoringClock();
        await using var services = Services(probe);
        using var worker = Worker(services, clock);
        await worker.StartAsync(default);
        try
        {
            await clock.TimerCreated.Task.WaitAsync(TestTimeout);
            clock.Advance(Interval);
            var first = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
            clock.Advance(TimeSpan.FromMinutes(5));
            Assert.False(probe.Runs.Reader.TryRead(out _));
            Assert.Equal(1, probe.MaxActive);
            release.SetResult();
            Assert.Equal(first.ScopeId, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
            var second = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
            Assert.NotEqual(first.ScopeId, second.ScopeId);
            Assert.Equal(second.ScopeId, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
            Assert.Equal(1, probe.MaxActive);
            Assert.False(probe.Runs.Reader.TryRead(out _));
        }
        finally { await StopAsync(worker); }
    }

    [Fact]
    public async Task Shutdown_interrupts_timer_without_waiting_for_another_interval()
    {
        var probe = new CycleProbe();
        var clock = new MonitoringClock();
        await using var services = Services(probe);
        using var worker = Worker(services, clock);
        await worker.StartAsync(default);
        await clock.TimerCreated.Task.WaitAsync(TestTimeout);

        await StopAsync(worker);

        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.False(probe.Runs.Reader.TryRead(out _));
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task Shutdown_cancels_current_cycle_and_disposes_its_scope()
    {
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new CycleProbe { Handler = (_, ct) => neverCompletes.Task.WaitAsync(ct) };
        var clock = new MonitoringClock();
        await using var services = Services(probe);
        using var worker = Worker(services, clock);
        await worker.StartAsync(default);
        await clock.TimerCreated.Task.WaitAsync(TestTimeout);
        clock.Advance(Interval);
        var cycle = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

        await StopAsync(worker);

        Assert.True(cycle.Token.IsCancellationRequested);
        Assert.Equal(cycle.ScopeId, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.False(probe.Runs.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Cycle_failure_is_retried_on_next_tick_with_a_new_scope()
    {
        var probe = new CycleProbe { Handler = (_, _) => Task.FromException(new InvalidOperationException("private database detail")) };
        var clock = new MonitoringClock();
        await using var services = Services(probe);
        using var worker = Worker(services, clock);
        await worker.StartAsync(default);
        try
        {
            await clock.TimerCreated.Task.WaitAsync(TestTimeout);
            clock.Advance(Interval);
            var failed = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
            Assert.Equal(failed.ScopeId, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
            probe.Handler = (_, _) => Task.CompletedTask;
            clock.Advance(Interval);
            var next = await probe.Runs.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
            Assert.NotEqual(failed.ScopeId, next.ScopeId);
            Assert.Equal(next.ScopeId, await probe.Disposed.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout));
        }
        finally { await StopAsync(worker); }
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("00:00:00.0000001")]
    [InlineData("50.00:00:00")]
    public async Task Invalid_timer_interval_fails_host_startup(string interval)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PriceAlertMonitoring:Interval"] = interval });
        builder.Services.AddPriceAlertMonitoring(builder.Configuration);
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Invalid_daily_budget_fails_host_startup(string budget)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PriceAlertMonitoring:DailyQuoteBudget"] = budget });
        builder.Services.AddPriceAlertMonitoring(builder.Configuration);
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void Registration_resolves_monitoring_service_scoped_and_worker_once()
    {
        var collection = new ServiceCollection();
        collection.AddPriceAlertMonitoring(new ConfigurationBuilder().Build());

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(collection, d => d.ServiceType == typeof(IPriceAlertMonitoringService)).Lifetime);
        Assert.Equal(typeof(PriceAlertMonitoringService), Assert.Single(collection, d => d.ServiceType == typeof(IPriceAlertMonitoringService)).ImplementationType);
        Assert.Equal(typeof(PriceAlertMonitoringWorker), Assert.Single(collection, d => d.ServiceType == typeof(IHostedService)).ImplementationType);
    }

    private static ServiceProvider Services(CycleProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddScoped<IPriceAlertMonitoringService, ScopedMonitor>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static PriceAlertMonitoringWorker Worker(ServiceProvider services, TimeProvider clock, TimeSpan? interval = null) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new PriceAlertMonitoringOptions { Interval = interval ?? Interval }),
            clock, NullLogger<PriceAlertMonitoringWorker>.Instance);

    private static async Task StopAsync(PriceAlertMonitoringWorker worker)
    {
        using var cancellation = new CancellationTokenSource(TestTimeout);
        await worker.StopAsync(cancellation.Token);
        await worker.ExecuteTask!.WaitAsync(TestTimeout);
    }

    private sealed record Cycle(Guid ScopeId, CancellationToken Token);

    private sealed class CycleProbe
    {
        public Channel<Cycle> Runs { get; } = Channel.CreateUnbounded<Cycle>();
        public Channel<Guid> Disposed { get; } = Channel.CreateUnbounded<Guid>();
        public Func<Cycle, CancellationToken, Task> Handler { get; set; } = (_, _) => Task.CompletedTask;
        public int Active;
        public int MaxActive;
    }

    private sealed class ScopedMonitor(CycleProbe probe) : IPriceAlertMonitoringService, IAsyncDisposable
    {
        private readonly Guid scopeId = Guid.NewGuid();
        public async Task<PriceAlertMonitoringResult> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref probe.Active);
            if (active > probe.MaxActive) probe.MaxActive = active;
            var cycle = new Cycle(scopeId, cancellationToken);
            probe.Runs.Writer.TryWrite(cycle);
            try
            {
                await probe.Handler(cycle, cancellationToken);
                return new(0, 0, 0, 0, []);
            }
            finally { Interlocked.Decrement(ref probe.Active); }
        }
        public ValueTask DisposeAsync()
        {
            probe.Disposed.Writer.TryWrite(scopeId);
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class MonitoringClock : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int ActiveTimerCount { get { lock (timers) return timers.Count; } }
    public override DateTimeOffset GetUtcNow() { lock (timers) return now; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (timers)
        {
            var timer = new ManualTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        ManualTimer[] due;
        lock (timers)
        {
            now += elapsed;
            due = timers.Where(t => t.Due <= now).ToArray();
            foreach (var timer in due) timer.Due = timer.Period > TimeSpan.Zero ? now + timer.Period : DateTimeOffset.MaxValue;
        }
        foreach (var timer in due) timer.Fire();
    }

    private sealed class ManualTimer(MonitoringClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due = DateTimeOffset.MaxValue;
        public TimeSpan Period;
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.timers)
            {
                if (disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock.now + dueTime;
                Period = period;
                return true;
            }
        }
        public void Fire() { lock (clock.timers) { if (!disposed) callback(state); } }
        public void Dispose() { lock (clock.timers) { disposed = true; clock.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
