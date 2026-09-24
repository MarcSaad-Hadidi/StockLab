using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockLab.Application.DTOs.Trading;
using StockLab.Application.Exceptions;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

public sealed class PaperTradingEngineTests
{
    [Fact]
    public async Task Buy_decreases_cash_adds_holding_and_records_transaction()
    {
        await using var fixture = await TradingFixture.CreateAsync();
        var engine = fixture.CreateEngine();

        var result = await engine.ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(Guid.NewGuid(), "BUY", " aapl ", 10m, 125m));

        Assert.Equal("BUY", result.Side);
        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal(1_250m, result.TotalAmount);
        Assert.Equal(98_750m, result.CashBalance);
        Assert.Equal(10m, result.HoldingQuantity);
        Assert.Equal(125m, result.AverageCost!.Value);

        var portfolio = await fixture.Context.Portfolios.AsNoTracking()
            .Include(row => row.Holdings)
            .SingleAsync(row => row.Id == fixture.PortfolioId);
        Assert.Equal(98_750m, portfolio.CashBalance);
        Assert.Single(portfolio.Holdings);
        Assert.Equal(1, await fixture.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Buy_rejects_an_order_that_exceeds_cash_without_persisting_changes()
    {
        await using var fixture = await TradingFixture.CreateAsync(initialCapital: 1_000m);
        var engine = fixture.CreateEngine();

        var error = await Assert.ThrowsAsync<PaperTradingException>(() => engine.ExecuteAsync(
            fixture.PortfolioId, new PaperTradeRequest(Guid.NewGuid(), "BUY", "AAPL", 11m, 100m)));

        Assert.Equal(PaperTradingFailure.InsufficientCash, error.Category);
        var portfolio = await fixture.Context.Portfolios.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.PortfolioId);
        Assert.Equal(1_000m, portfolio.CashBalance);
        Assert.Equal(0, await fixture.Context.Holdings.CountAsync());
        Assert.Equal(0, await fixture.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Sell_increases_cash_and_removes_a_fully_sold_holding()
    {
        await using var fixture = await TradingFixture.CreateAsync();
        await fixture.AddHoldingAsync("MSFT", 10m, 200m);
        var engine = fixture.CreateEngine();

        var result = await engine.ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(Guid.NewGuid(), "SELL", "MSFT", 10m, 240m));

        Assert.Equal("SELL", result.Side);
        Assert.Equal(102_400m, result.CashBalance);
        Assert.Equal(0m, result.HoldingQuantity);
        Assert.Null(result.AverageCost);
        Assert.Equal(0, await fixture.Context.Holdings.CountAsync());
        Assert.Equal(1, await fixture.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Sell_rejects_a_quantity_above_the_current_holding()
    {
        await using var fixture = await TradingFixture.CreateAsync();
        await fixture.AddHoldingAsync("NVDA", 2m, 100m);
        var engine = fixture.CreateEngine();

        var error = await Assert.ThrowsAsync<PaperTradingException>(() => engine.ExecuteAsync(
            fixture.PortfolioId, new PaperTradeRequest(Guid.NewGuid(), "SELL", "NVDA", 2.01m, 110m)));

        Assert.Equal(PaperTradingFailure.InsufficientHoldings, error.Category);
        var holding = await fixture.Context.Holdings.AsNoTracking().SingleAsync();
        Assert.Equal(2m, holding.Quantity);
        Assert.Equal(100_000m, await fixture.Context.Portfolios.Select(row => row.CashBalance).SingleAsync());
        Assert.Equal(0, await fixture.Context.Transactions.CountAsync());
    }

    [Fact]
    public async Task Multiple_buys_update_the_weighted_average_cost()
    {
        await using var fixture = await TradingFixture.CreateAsync(initialCapital: 2_000m);
        var engine = fixture.CreateEngine();

        await engine.ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(Guid.NewGuid(), "BUY", "AAPL", 10m, 100m));
        var result = await engine.ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(Guid.NewGuid(), "BUY", "AAPL", 5m, 130m));

        Assert.Equal(15m, result.HoldingQuantity);
        Assert.Equal(110m, result.AverageCost!.Value);
        Assert.Equal(350m, result.CashBalance);

        var holding = await fixture.Context.Holdings.AsNoTracking().SingleAsync();
        Assert.Equal(15m, holding.Quantity);
        Assert.Equal(110m, holding.AverageCost);
    }

    [Fact]
    public async Task Repeating_the_same_order_is_idempotent_but_a_changed_retry_is_rejected()
    {
        await using var fixture = await TradingFixture.CreateAsync();
        var engine = fixture.CreateEngine();
        var orderId = Guid.NewGuid();
        var request = new PaperTradeRequest(orderId, "BUY", "AAPL", 2m, 100m);

        var first = await engine.ExecuteAsync(fixture.PortfolioId, request);
        var retry = await engine.ExecuteAsync(fixture.PortfolioId, request);

        Assert.Equal(first.TransactionId, retry.TransactionId);
        Assert.Equal(99_800m, retry.CashBalance);
        Assert.Equal(1, await fixture.Context.Transactions.CountAsync());

        var error = await Assert.ThrowsAsync<PaperTradingException>(() => engine.ExecuteAsync(
            fixture.PortfolioId, request with { Quantity = 3m }));
        Assert.Equal(PaperTradingFailure.DuplicateOrder, error.Category);
    }

    [Theory]
    [InlineData("BUY", 0, 99_800, 2, 100)]
    [InlineData("BUY", 2, 99_800, 4, 90)]
    [InlineData("SELL", 4, 100_200, 2, 80)]
    [InlineData("SELL", 2, 100_200, 0, null)]
    public async Task Duplicate_committed_during_lookup_returns_fresh_cash_and_holdings(
        string side, int initialQuantity, int expectedCash, int expectedQuantity, int? expectedAverageCost)
    {
        var lookupInterceptor = new ConcurrentOrderLookupInterceptor();
        await using var fixture = await TradingFixture.CreateAsync(lookupInterceptor: lookupInterceptor);
        if (initialQuantity > 0)
        {
            await fixture.AddHoldingAsync("AAPL", initialQuantity, 80m);
        }

        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var now = TradingFixture.FixedUtcNow;
        // SQLite serializes writers. Inject the winner's SQL changes at the lookup boundary
        // to model SQL Server READ COMMITTED without refreshing the retry's tracked entities.
        lookupInterceptor.BeforeLookup = async cancellationToken =>
        {
            await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE Portfolios SET CashBalance = {expectedCash} WHERE Id = {fixture.PortfolioId}
                """, cancellationToken);
            if (expectedQuantity == 0)
            {
                await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM Holdings WHERE PortfolioId = {fixture.PortfolioId} AND Symbol = {"AAPL"}
                    """, cancellationToken);
            }
            else if (initialQuantity == 0)
            {
                await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO Holdings (Id, PortfolioId, Symbol, Quantity, AverageCost, UpdatedAtUtc)
                    VALUES ({Guid.NewGuid()}, {fixture.PortfolioId}, {"AAPL"},
                            {expectedQuantity}, {expectedAverageCost}, {now})
                    """, cancellationToken);
            }
            else
            {
                await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Holdings SET Quantity = {expectedQuantity}, AverageCost = {expectedAverageCost},
                        UpdatedAtUtc = {now}
                    WHERE PortfolioId = {fixture.PortfolioId} AND Symbol = {"AAPL"}
                    """, cancellationToken);
            }

            await fixture.Context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Transactions
                    (Id, PortfolioId, OrderId, Side, Symbol, Quantity, ExecutionPrice, TotalAmount, ExecutedAtUtc)
                VALUES ({transactionId}, {fixture.PortfolioId}, {orderId}, {side}, {"AAPL"},
                        {2m}, {100m}, {200m}, {now})
                """, cancellationToken);
        };

        var result = await fixture.CreateEngine().ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(orderId, side, " aapl ", 2m, 100m));

        Assert.Equal(transactionId, result.TransactionId);
        Assert.Equal((decimal)expectedCash, result.CashBalance);
        Assert.Equal((decimal)expectedQuantity, result.HoldingQuantity);
        Assert.Equal((decimal?)expectedAverageCost, result.AverageCost);
        Assert.Single(await fixture.Context.Transactions.AsNoTracking().ToListAsync());

        var persistedPortfolio = await fixture.Context.Portfolios.AsNoTracking()
            .Include(row => row.Holdings).SingleAsync(row => row.Id == fixture.PortfolioId);
        Assert.Equal((decimal)expectedCash, persistedPortfolio.CashBalance);
        if (expectedQuantity == 0)
        {
            Assert.Empty(persistedPortfolio.Holdings);
        }
        else
        {
            var holding = Assert.Single(persistedPortfolio.Holdings);
            Assert.Equal((decimal)expectedQuantity, holding.Quantity);
            Assert.Equal((decimal)expectedAverageCost!.Value, holding.AverageCost);
        }
    }

    [Theory]
    [InlineData(SimulatedSaveFailure.Concurrency)]
    [InlineData(SimulatedSaveFailure.Update)]
    public async Task Concurrent_duplicate_order_recovers_after_disposing_the_rolled_back_transaction(
        SimulatedSaveFailure saveFailure)
    {
        await using var fixture = await TradingFixture.CreateAsync();
        var orderId = Guid.NewGuid();
        var portfolio = await fixture.Context.Portfolios.SingleAsync(row => row.Id == fixture.PortfolioId);
        portfolio.CashBalance = 99_800m;
        fixture.Context.Holdings.Add(new Holding
        {
            Id = Guid.NewGuid(),
            PortfolioId = fixture.PortfolioId,
            Symbol = "AAPL",
            Quantity = 2m,
            AverageCost = 100m,
            UpdatedAtUtc = TradingFixture.FixedUtcNow
        });
        fixture.Context.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            PortfolioId = fixture.PortfolioId,
            OrderId = orderId,
            Side = "BUY",
            Symbol = "AAPL",
            Quantity = 2m,
            ExecutionPrice = 100m,
            TotalAmount = 200m,
            ExecutedAtUtc = TradingFixture.FixedUtcNow
        });
        await fixture.Context.SaveChangesAsync();

        fixture.Context.HideTransactions = true;
        fixture.Context.SaveFailure = saveFailure;
        var result = await fixture.CreateEngine().ExecuteAsync(fixture.PortfolioId,
            new PaperTradeRequest(orderId, "BUY", "AAPL", 2m, 100m));

        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(99_800m, result.CashBalance);
        Assert.Equal(2m, result.HoldingQuantity);
        Assert.Single(await fixture.Context.Transactions.ToListAsync());
    }

    [Theory]
    [InlineData("BUY", 0, true, true)]
    [InlineData("BUY", 0, true, false)]
    [InlineData("BUY", 4, true, true)]
    [InlineData("BUY", 4, true, false)]
    [InlineData("SELL", 4, true, true)]
    [InlineData("SELL", 4, true, false)]
    [InlineData("SELL", 2, true, true)]
    [InlineData("SELL", 2, true, false)]
    [InlineData("BUY", 0, false, true)]
    [InlineData("BUY", 0, false, false)]
    public async Task Failed_execution_does_not_leak_changes_into_the_next_order(
        string side, int initialQuantity, bool failDuringCommit, bool cancelExecution)
    {
        var commitInterceptor = new CommitFailureInterceptor();
        await using var fixture = await TradingFixture.CreateAsync(commitInterceptor: commitInterceptor);
        if (initialQuantity > 0)
        {
            await fixture.AddHoldingAsync("AAPL", initialQuantity, 80m);
        }

        var engine = fixture.CreateEngine();
        using var cancellation = new CancellationTokenSource();
        Exception failure = cancelExecution
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("Simulated execution failure.");
        void FailExecution()
        {
            if (cancelExecution)
            {
                cancellation.Cancel();
            }

            throw failure;
        }

        if (failDuringCommit)
        {
            // SaveChanges has already written to the real SQLite transaction at this boundary.
            commitInterceptor.BeforeCommit = FailExecution;
        }
        else
        {
            fixture.Context.BeforeSave = FailExecution;
        }

        var failedOrder = new PaperTradeRequest(Guid.NewGuid(), side, "AAPL", 2m, 100m);
        var error = await Record.ExceptionAsync(() => engine.ExecuteAsync(
            fixture.PortfolioId, failedOrder, cancellation.Token));

        Assert.Same(failure, error);
        Assert.Null(fixture.Context.Database.CurrentTransaction);
        var rolledBackPortfolio = await fixture.Context.Portfolios.AsNoTracking()
            .Include(row => row.Holdings).SingleAsync();
        Assert.Equal(100_000m, rolledBackPortfolio.CashBalance);
        Assert.Equal((decimal)initialQuantity, rolledBackPortfolio.Holdings.Sum(row => row.Quantity));
        Assert.Empty(await fixture.Context.Transactions.AsNoTracking().ToListAsync());

        // Reuse both engine and context, without clearing or reloading tracked entities in the test.
        var nextOrder = new PaperTradeRequest(Guid.NewGuid(), "BUY", "AAPL", 1m, 120m);
        var result = await engine.ExecuteAsync(fixture.PortfolioId, nextOrder);
        var expectedAverageCost = decimal.Round(
            (initialQuantity * 80m + 120m) / (initialQuantity + 1), 4, MidpointRounding.AwayFromZero);

        Assert.Equal(99_880m, result.CashBalance);
        Assert.Equal(initialQuantity + 1m, result.HoldingQuantity);
        Assert.Equal(expectedAverageCost, result.AverageCost);

        var persistedPortfolio = await fixture.Context.Portfolios.AsNoTracking()
            .Include(row => row.Holdings).SingleAsync();
        Assert.Equal(99_880m, persistedPortfolio.CashBalance);
        var holding = Assert.Single(persistedPortfolio.Holdings);
        Assert.Equal(initialQuantity + 1m, holding.Quantity);
        Assert.Equal(expectedAverageCost, holding.AverageCost);
        var transaction = Assert.Single(await fixture.Context.Transactions.AsNoTracking().ToListAsync());
        Assert.Equal(nextOrder.OrderId, transaction.OrderId);
        Assert.Equal(result.TransactionId, transaction.Id);
    }

    private sealed class TradingFixture(SqliteConnection connection, SqliteTradingDbContext context,
        Guid portfolioId) : IAsyncDisposable
    {
        public static readonly DateTime FixedUtcNow = new(2026, 9, 24, 18, 0, 0, DateTimeKind.Utc);

        public SqliteTradingDbContext Context { get; } = context;
        public Guid PortfolioId { get; } = portfolioId;

        public static async Task<TradingFixture> CreateAsync(
            decimal initialCapital = 100_000m, DbCommandInterceptor? lookupInterceptor = null,
            DbTransactionInterceptor? commitInterceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var transactionLifecycle = new TransactionLifecycleState();
            var optionsBuilder = new DbContextOptionsBuilder<StockLabDbContext>()
                .UseSqlite(connection)
                .LogTo(
                    (eventId, _) => eventId == RelationalEventId.TransactionRolledBack
                                    || eventId == RelationalEventId.TransactionDisposed,
                    transactionLifecycle.Record)
                .AddInterceptors(new CompletedTransactionGuardInterceptor(transactionLifecycle));
            if (lookupInterceptor is not null)
            {
                optionsBuilder.AddInterceptors(lookupInterceptor);
            }
            if (commitInterceptor is not null)
            {
                optionsBuilder.AddInterceptors(commitInterceptor);
            }

            var context = new SqliteTradingDbContext(optionsBuilder.Options);
            await context.Database.EnsureCreatedAsync();

            var userId = Guid.NewGuid();
            var portfolioId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                DisplayName = "Test User",
                Email = "test@example.com",
                NormalizedEmail = "TEST@EXAMPLE.COM",
                PasswordHash = "test-hash",
                CreatedAtUtc = FixedUtcNow,
                UpdatedAtUtc = FixedUtcNow,
                Version = new byte[8]
            };
            context.Users.Add(user);
            context.Portfolios.Add(new Portfolio
            {
                Id = portfolioId,
                UserId = userId,
                User = user,
                Currency = "USD",
                InitialCapital = initialCapital,
                CashBalance = initialCapital,
                CreatedAtUtc = FixedUtcNow,
                Version = new byte[8]
            });
            await context.SaveChangesAsync();
            return new TradingFixture(connection, context, portfolioId);
        }

        public PaperTradingEngine CreateEngine() => new(Context, new FixedTimeProvider(FixedUtcNow));

        public async Task AddHoldingAsync(string symbol, decimal quantity, decimal averageCost)
        {
            Context.Holdings.Add(new Holding
            {
                Id = Guid.NewGuid(),
                PortfolioId = PortfolioId,
                Symbol = symbol,
                Quantity = quantity,
                AverageCost = averageCost,
                UpdatedAtUtc = FixedUtcNow
            });
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class SqliteTradingDbContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        public bool HideTransactions { get; set; }
        public bool RecoveryQueriesRequireDisposedTransaction { get; private set; }
        public SimulatedSaveFailure SaveFailure { get; set; }
        public Action? BeforeSave { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().Property(user => user.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(portfolio => portfolio.Version).ValueGeneratedNever();
            modelBuilder.Entity<Transaction>().HasQueryFilter(transaction => !HideTransactions);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var beforeSave = BeforeSave;
            BeforeSave = null;
            beforeSave?.Invoke();

            if (SaveFailure != SimulatedSaveFailure.None)
            {
                HideTransactions = false;
                RecoveryQueriesRequireDisposedTransaction = true;
                if (SaveFailure == SimulatedSaveFailure.Concurrency)
                {
                    throw new DbUpdateConcurrencyException("Simulated concurrent paper-trading order.");
                }

                throw new DbUpdateException("Simulated duplicate paper-trading order.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CommitFailureInterceptor : DbTransactionInterceptor
    {
        public Action? BeforeCommit { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            var beforeCommit = BeforeCommit;
            BeforeCommit = null;
            beforeCommit?.Invoke();
            return new ValueTask<InterceptionResult>(result);
        }
    }

    private sealed class TransactionLifecycleState
    {
        public bool RolledBackTransactionAwaitingDisposal { get; private set; }

        public void Record(EventData eventData)
        {
            if (eventData.EventId == RelationalEventId.TransactionRolledBack)
            {
                RolledBackTransactionAwaitingDisposal = true;
            }
            else if (eventData.EventId == RelationalEventId.TransactionDisposed)
            {
                RolledBackTransactionAwaitingDisposal = false;
            }
        }
    }

    private sealed class ConcurrentOrderLookupInterceptor : DbCommandInterceptor
    {
        public Func<CancellationToken, Task>? BeforeLookup { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (BeforeLookup is { } beforeLookup
                && command.CommandText.Contains("FROM \"Transactions\"", StringComparison.Ordinal))
            {
                BeforeLookup = null;
                await beforeLookup(cancellationToken);
            }

            return result;
        }
    }

    private sealed class CompletedTransactionGuardInterceptor(TransactionLifecycleState transactionLifecycle)
        : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is SqliteTradingDbContext
                {
                    RecoveryQueriesRequireDisposedTransaction: true
                }
                && transactionLifecycle.RolledBackTransactionAwaitingDisposal)
            {
                throw new InvalidOperationException(
                    "Recovery queries cannot reuse a completed database transaction.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    public enum SimulatedSaveFailure
    {
        None,
        Concurrency,
        Update
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
