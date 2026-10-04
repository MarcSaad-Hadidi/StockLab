using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Application.DTOs.MarketData;
using StockLab.Application.Interfaces;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence;
using StockLab.Infrastructure.Trading;

namespace StockLab.UnitTests.Trading;

internal sealed class AiCurrentPositionsFixture(string? sqlConnection, SqliteConnection? sqliteConnection)
    : IDbContextFactory<StockLabDbContext>, IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    public Market ExecutionMarket { get; } = new();
    public Market ValuationMarket { get; } = new();
    public ReadObserver Observer { get; } = new();

    public StockLabDbContext CreateDbContext() => CreateContext();
    private StockLabDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<StockLabDbContext>();
        if (sqlConnection is not null) builder.UseSqlServer(sqlConnection);
        else builder.UseSqlite(sqliteConnection!);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return sqlConnection is not null ? new StockLabDbContext(builder.Options) : new SqliteContext(builder.Options);
    }

    public IAiCurrentPositionsService Service() => new AiCurrentPositionsService(
        new AiTraderPortfolioService(new ReadFactory(this), ValuationMarket, new Clock()));

    public Task<AiTraderPortfolioState> InitializeAsync() =>
        new AiTraderPortfolioService(this, ValuationMarket, new Clock()).GetOrCreateAsync();

    public Task<AiTradeExecutionResult> ExecuteAsync(string symbol = "AAPL", decimal quantity = 10m,
        decimal price = 100m, AiTradingSignal signal = AiTradingSignal.Buy, IInterceptor? interceptor = null)
    {
        ExecutionMarket.Prices[symbol] = price;
        IDbContextFactory<StockLabDbContext> factory = interceptor is null ? this : new InterceptedFactory(this, interceptor);
        IAiTradeExecutionService engine = new AiPaperTradingEngine(factory, ExecutionMarket,
            Options.Create(new AiRiskOptions()), new Clock());
        return engine.ExecuteAsync(new(Guid.NewGuid(), new(true, symbol, signal, 0.9m, price, quantity, null)));
    }

    public async Task RecordDecisionAsync(AiTradingSignal signal, AiRiskRejectionReason? reason = null)
    {
        var request = new AiDecisionRecordRequest(Guid.NewGuid(), "AAPL", signal, 0.9m,
            new(2026, 10, 2), "test-model", "test-version");
        await new AiDecisionHistoryService(this, new Clock()).RecordAsync(request);
        if (reason is not null)
            await new AiRejectedDecisionHistoryService(this, new Clock()).RecordAsync(new(request.DecisionId,
                new(false, request.Symbol, signal, request.Confidence, 100m, 0m, reason)));
    }

    public async Task<string> StateAsync()
    {
        // Fresh detached reads include all scalar fields, timestamps and SQL Server rowversions.
        await using var db = CreateDbContext();
        return JsonSerializer.Serialize(new
        {
            AiPortfolios = await db.AiTraderPortfolios.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            AiPositions = await db.AiTraderPositions.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            AiTrades = await db.AiTrades.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            AiDecisions = await db.AiDecisions.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            Rejections = await db.AiRejectedDecisions.AsNoTracking().OrderBy(p => p.AiDecisionId).ToArrayAsync(),
            Users = await db.Users.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            Portfolios = await db.Portfolios.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            Holdings = await db.Holdings.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            Transactions = await db.Transactions.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            Watchlists = await db.Watchlists.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(),
            Alerts = await db.PriceAlerts.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync()
        });
    }

    public static async Task<AiCurrentPositionsFixture> CreateAsync(bool sqlServer = false)
    {
        // Every SQL test owns a random local database; never use application/Azure credentials.
        var connection = sqlServer ? null : new SqliteConnection("Data Source=:memory:");
        if (connection is not null) await connection.OpenAsync();
        var f = new AiCurrentPositionsFixture(sqlServer
            ? $"Server=(localdb)\\MSSQLLocalDB;Database=StockLabCurrentPositionsTests_{Guid.NewGuid():N};Integrated Security=true;TrustServerCertificate=true"
            : null, connection);
        await using var db = f.CreateDbContext();
        if (sqlServer) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();
        return f;
    }

    public async ValueTask DisposeAsync()
    {
        if (sqliteConnection is not null) await sqliteConnection.DisposeAsync();
        else
        {
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class ReadFactory(AiCurrentPositionsFixture fixture) : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => fixture.CreateContext(fixture.Observer);
    }

    private sealed class InterceptedFactory(AiCurrentPositionsFixture fixture, IInterceptor interceptor)
        : IDbContextFactory<StockLabDbContext>
    {
        public StockLabDbContext CreateDbContext() => fixture.CreateContext(interceptor);
    }

    private sealed class SqliteContext(DbContextOptions<StockLabDbContext> options) : StockLabDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AiTraderPortfolio>().Property(p => p.Version).HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<User>().Property(p => p.Version).ValueGeneratedNever();
            modelBuilder.Entity<Portfolio>().Property(p => p.Version).ValueGeneratedNever();
            modelBuilder.Entity<PriceAlert>().Property(p => p.Version).ValueGeneratedNever();
        }
    }

    internal sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class Market : IMarketDataProvider
    {
        public Dictionary<string, decimal> Prices { get; } = new(StringComparer.Ordinal) { ["AAPL"] = 120m };
        public Func<string, CancellationToken, StockQuote?>? Quote { get; set; }
        public List<string> Calls { get; } = [];
        public CancellationToken LastToken { get; private set; }
        public Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
        {
            Calls.Add(symbol);
            LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Quote is null ? new(symbol, "USD", Prices[symbol], null, null, null, Now)
                : Quote(symbol, cancellationToken));
        }
        public Task<IReadOnlyList<StockSearchResult>> SearchStocksAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockHistory?> GetHistoryAsync(StockHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class ReadObserver : DbCommandInterceptor, ISaveChangesInterceptor
    {
        public List<string> Commands { get; } = [];
        public int SaveCalls { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
        public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            SaveCalls++;
            throw new InvalidOperationException("Current positions must never save changes.");
        }
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            throw new InvalidOperationException("Current positions must never save changes.");
        }
    }
}
