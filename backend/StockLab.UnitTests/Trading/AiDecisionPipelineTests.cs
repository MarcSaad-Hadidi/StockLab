using Microsoft.EntityFrameworkCore;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class AiDecisionPipelineTests
{
    internal static readonly DateOnly Session = new(2026, 10, 2);
    internal static AiInferenceBatch Batch() => new("random_forest_baseline", "v000001", Session,
        [new("AAPL", AiTradingSignal.Buy, .82m), new("MSFT", AiTradingSignal.Sell, .91m),
         new("NVDA", AiTradingSignal.Hold, .93m)]);

    [Fact]
    public async Task All_signals_use_real_append_only_history_and_retry_without_changing_other_state()
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync();
        var inference = new Inference();
        var history = new AiDecisionHistoryService(db, new AiCurrentPositionsFixture.Clock());
        var service = new AiDecisionPipelineService(inference, history, new AiCurrentPositionsFixture.Clock());
        var first = await service.RunAsync(Session);
        inference.Batch = inference.Batch with { Decisions = inference.Batch.Decisions.Reverse().ToArray() };
        var retry = await service.RunAsync(Session);

        Assert.Equal(first.OrderBy(d => d.Id), retry.OrderBy(d => d.Id));
        Assert.Equal(3, (await history.GetRecentAsync(10)).Count);
        Assert.Equal([AiTradingSignal.Buy, AiTradingSignal.Sell, AiTradingSignal.Hold], first.Select(d => d.Signal));
        Assert.All(first, d => { Assert.Equal("random_forest_baseline", d.ModelName);
            Assert.Equal("v000001", d.ModelVersion); Assert.Equal(Session, d.DecisionDate); Assert.NotEqual(Guid.Empty, d.Id); });
        Assert.Equal(.93m, first[2].Confidence);
        await using var reader = db.CreateDbContext();
        Assert.Empty(await reader.AiTraderPortfolios.ToArrayAsync());
        Assert.Empty(await reader.AiTrades.ToArrayAsync());
        Assert.Empty(await reader.AiRejectedDecisions.ToArrayAsync());
        Assert.Empty(await reader.Users.ToArrayAsync());
        Assert.Empty(db.ExecutionMarket.Calls);
        Assert.Empty(db.ValuationMarket.Calls);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("date")]
    [InlineData("model")]
    [InlineData("version")]
    [InlineData("duplicate")]
    [InlineData("confidence")]
    [InlineData("signal")]
    [InlineData("symbol")]
    public async Task Entire_batch_is_validated_before_any_history_write(string invalid)
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync();
        var batch = Batch();
        batch = invalid switch
        {
            "empty" => batch with { Decisions = [] },
            "date" => batch with { DecisionDate = Session.AddDays(-1) },
            "model" => batch with { ModelName = " " },
            "version" => batch with { ModelVersion = " " },
            "duplicate" => batch with { Decisions = [batch.Decisions[0], batch.Decisions[0]] },
            "confidence" => batch with { Decisions = [batch.Decisions[0], new("MSFT", AiTradingSignal.Buy, 1.1m)] },
            "signal" => batch with { Decisions = [batch.Decisions[0], new("MSFT", (AiTradingSignal)99, .8m)] },
            _ => batch with { Decisions = [batch.Decisions[0], new(" bad symbol", AiTradingSignal.Hold, .8m)] }
        };
        var service = new AiDecisionPipelineService(new Inference { Batch = batch },
            new AiDecisionHistoryService(db, new AiCurrentPositionsFixture.Clock()), new AiCurrentPositionsFixture.Clock());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RunAsync(Session));
        await using var reader = db.CreateDbContext();
        Assert.Empty(await reader.AiDecisions.ToArrayAsync());
    }

    [Fact]
    public async Task Session_must_be_closed_before_UTC_today_and_cancellation_precedes_inference()
    {
        var inference = new Inference();
        var service = new AiDecisionPipelineService(inference, null!, new AiCurrentPositionsFixture.Clock());
        foreach (var day in new[] { default(DateOnly), Session.AddDays(1), Session.AddDays(2) })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RunAsync(day));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(Session, cancelled.Token));
        Assert.Equal(0, inference.Calls);
    }

    [Fact]
    public async Task Inference_failure_does_not_write_or_invent_a_hold_decision()
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync();
        var inference = new Inference { Failure = new InvalidOperationException("private marker") };
        var service = new AiDecisionPipelineService(inference,
            new AiDecisionHistoryService(db, new AiCurrentPositionsFixture.Clock()), new AiCurrentPositionsFixture.Clock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(Session));
        await using var reader = db.CreateDbContext();
        Assert.Empty(await reader.AiDecisions.ToArrayAsync());
    }

    [Fact]
    public async Task Same_version_symbol_and_session_with_changed_inference_conflicts_instead_of_overwriting()
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync();
        var inference = new Inference();
        var history = new AiDecisionHistoryService(db, new AiCurrentPositionsFixture.Clock());
        var service = new AiDecisionPipelineService(inference, history, new AiCurrentPositionsFixture.Clock());
        var before = await service.RunAsync(Session);
        inference.Batch = inference.Batch with { Decisions = [new("AAPL", AiTradingSignal.Buy, .83m)] };
        var conflict = await Assert.ThrowsAsync<AiDecisionHistoryException>(() => service.RunAsync(Session));
        Assert.Equal(AiDecisionHistoryFailure.DecisionConflict, conflict.Category);
        Assert.Equal(before.OrderBy(d => d.Id), (await history.GetRecentAsync(10)).OrderBy(d => d.Id));
    }

    [Fact]
    public async Task Version_and_session_are_part_of_stable_identity()
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync();
        var inference = new Inference();
        var service = new AiDecisionPipelineService(inference,
            new AiDecisionHistoryService(db, new AiCurrentPositionsFixture.Clock()), new AiCurrentPositionsFixture.Clock());
        var first = await service.RunAsync(Session);
        inference.Batch = inference.Batch with { ModelVersion = "v000002" };
        var second = await service.RunAsync(Session);
        inference.Batch = inference.Batch with { DecisionDate = Session.AddDays(-1) };
        var third = await service.RunAsync(Session.AddDays(-1));
        Assert.Equal(9, first.Concat(second).Concat(third).Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public async Task Partial_storage_failure_is_resumable_without_duplicate_prefix()
    {
        await using var db = await AiCurrentPositionsFixture.CreateAsync();
        var history = new AiDecisionHistoryService(db, new AiCurrentPositionsFixture.Clock());
        var failing = new FailSecondWrite(history);
        var service = new AiDecisionPipelineService(new Inference(), failing, new AiCurrentPositionsFixture.Clock());
        await Assert.ThrowsAsync<IOException>(() => service.RunAsync(Session));
        var prefix = Assert.Single(await history.GetRecentAsync(10));
        var resumed = await service.RunAsync(Session);
        Assert.Equal(3, (await history.GetRecentAsync(10)).Count);
        Assert.Equal(prefix, resumed.Single(d => d.Id == prefix.Id));
    }

    internal sealed class Inference : IAiDecisionInferenceProvider
    {
        public AiInferenceBatch Batch { get; set; } = AiDecisionPipelineTests.Batch();
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }
        public Task<AiInferenceBatch> InferAsync(DateOnly session, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Failure is null ? Task.FromResult(Batch) : Task.FromException<AiInferenceBatch>(Failure);
        }
    }

    private sealed class FailSecondWrite(IAiDecisionHistoryService history) : IAiDecisionHistoryService
    {
        private int writes;
        public Task<AiDecisionRecord> RecordAsync(AiDecisionRecordRequest request, CancellationToken cancellationToken = default) =>
            ++writes == 2 ? throw new IOException("test-only interruption") : history.RecordAsync(request, cancellationToken);
        public Task<AiDecisionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => history.GetByIdAsync(id, cancellationToken);
        public Task<IReadOnlyList<AiDecisionRecord>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => history.GetRecentAsync(limit, cancellationToken);
    }
}
