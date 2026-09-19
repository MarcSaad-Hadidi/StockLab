using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StockLab.Application.Exceptions;
using StockLab.Infrastructure.MarketEnrichment;

namespace StockLab.UnitTests.MarketData;

public sealed class AlphaVantageQueueTests
{
    [Fact]
    public async Task Queued_deadlines_expire_without_transport_or_budget_and_gate_recovers()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Respond = async (_, _) => { await release.Task; return Response(); };
        var active = fixture.Provider.GetFundamentalsAsync("ACTIVE");
        var queued = Enumerable.Range(0, 4).Select(i => fixture.Provider.GetFundamentalsAsync("Q" + i)).ToArray();
        try
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            foreach (var task in queued)
            {
                var error = await Assert.ThrowsAsync<MarketEnrichmentException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Equal(MarketEnrichmentFailure.Timeout, error.Category);
            }
            Assert.Equal(1, fixture.Handler.Calls);
            var repeated = await Assert.ThrowsAsync<MarketEnrichmentException>(() => fixture.Provider.GetFundamentalsAsync("Q0"));
            Assert.Equal(MarketEnrichmentFailure.Timeout, repeated.Category);
            Assert.Equal(1, fixture.Handler.Calls);
        }
        finally
        {
            release.TrySetResult();
            await Ignore(Task.WhenAll(queued.Append(active)));
        }
        fixture.Handler.Respond = (_, _) => Task.FromResult(Response());
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.Provider.GetFundamentalsAsync("Q0");
        Assert.Equal(2, fixture.Handler.Calls); // budget=2: unsent queue entries must cost nothing.
    }

    [Fact]
    public async Task Http_receives_only_time_remaining_after_queue_wait()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken httpToken = default;
        fixture.Handler.Respond = async (call, token) =>
        {
            if (call == 1) await release.Task;
            else { httpToken = token; entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return Response();
        };
        var first = fixture.Provider.GetFundamentalsAsync("FIRST");
        var second = fixture.Provider.GetFundamentalsAsync("SECOND");
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(600));
        release.SetResult();
        await first;
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(httpToken.IsCancellationRequested);
        var error = await Assert.ThrowsAsync<MarketEnrichmentException>(() => second.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(MarketEnrichmentFailure.Timeout, error.Category);
        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Last_active_caller_cancellation_releases_gate_and_sent_attempt_counts()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Respond = async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Response();
        };
        using var cancel = new CancellationTokenSource();
        var abandoned = fixture.Provider.GetFundamentalsAsync("ACTIVE", cancel.Token);
        await entered.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        fixture.Handler.Respond = (_, _) => Task.FromResult(Response());
        await fixture.Provider.GetFundamentalsAsync("NEXT").WaitAsync(TimeSpan.FromSeconds(2));
        var exhausted = await Assert.ThrowsAsync<MarketEnrichmentException>(() => fixture.Provider.GetFundamentalsAsync("THIRD"));
        Assert.Equal(MarketEnrichmentFailure.LocalBudgetExceeded, exhausted.Category);
        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Last_queued_caller_cancellation_removes_work_without_spending_budget()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Respond = async (_, _) => { await release.Task; return Response(); };
        var first = fixture.Provider.GetFundamentalsAsync("FIRST");
        using var cancel = new CancellationTokenSource();
        var abandoned = fixture.Provider.GetFundamentalsAsync("ABANDONED", cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        release.SetResult();
        await first;
        fixture.Handler.Respond = (_, _) => Task.FromResult(Response());
        await fixture.Provider.GetFundamentalsAsync("NEXT");
        Assert.Equal(2, fixture.Handler.Calls);
        // A fresh caller for the abandoned symbol may run; cancellation is never cached.
        fixture.Clock.Advance(TimeSpan.FromDays(1));
        await fixture.Provider.GetFundamentalsAsync("ABANDONED");
        Assert.Equal(3, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Cancelling_one_queued_joiner_preserves_other_callers()
    {
        using var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Respond = async (_, _) => { await release.Task; return Response(); };
        var first = fixture.Provider.GetFundamentalsAsync("FIRST");
        using var cancel = new CancellationTokenSource();
        var abandoned = fixture.Provider.GetFundamentalsAsync("SHARED", cancel.Token);
        var survivor = fixture.Provider.GetFundamentalsAsync("SHARED");
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        release.SetResult();
        await Task.WhenAll(first, survivor);
        Assert.Equal(2, fixture.Handler.Calls);
        await fixture.Provider.GetFundamentalsAsync("SHARED");
        Assert.Equal(2, fixture.Handler.Calls);
    }

    private static async Task Ignore(Task task) { try { await task; } catch { /* Drain failed work before disposing its gate. */ } }
    private static HttpResponseMessage Response() => new(HttpStatusCode.OK) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public Func<int, CancellationToken, Task<HttpResponseMessage>> Respond = (_, _) => Task.FromResult(Response());
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Respond(Interlocked.Increment(ref Calls), token);
    }
    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://www.alphavantage.co/"), Timeout = Timeout.InfiniteTimeSpan };
    }
    private sealed class Fixture : IDisposable
    {
        public readonly ManualClock Clock = new();
        public readonly Handler Handler = new();
        public readonly AlphaVantageProvider Provider;
        public Fixture()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AlphaVantage:ApiKey"] = "ALPHA-TEST-KEY" }).Build();
            Provider = new(new Factory(Handler), config, Options.Create(new AlphaVantageOptions { TimeoutSeconds = 1, DailyRequestBudget = 2 }), Clock);
        }
        public void Dispose() { Provider.Dispose(); Handler.Dispose(); }
    }
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> timers = [];
        public override DateTimeOffset GetUtcNow() { lock (timers) return now; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (timers)
            {
                var timer = new ManualTimer(this, callback, state);
                timer.Change(dueTime, period);
                timers.Add(timer);
                return timer;
            }
        }
        public void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (timers) { now += duration; due = timers.Where(t => t.Due <= now).ToArray(); }
            foreach (var timer in due) timer.Fire();
        }
        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset Due = DateTimeOffset.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock.timers) Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock.now + dueTime;
                return true;
            }
            public void Fire() { Dispose(); callback(state); }
            public void Dispose() { lock (clock.timers) { Due = DateTimeOffset.MaxValue; clock.timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
