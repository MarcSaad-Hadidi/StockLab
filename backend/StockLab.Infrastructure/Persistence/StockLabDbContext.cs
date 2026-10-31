using Microsoft.EntityFrameworkCore;
using StockLab.Domain.Entities;
using StockLab.Infrastructure.Persistence.Configurations;

namespace StockLab.Infrastructure.Persistence;

public class StockLabDbContext(DbContextOptions<StockLabDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<PriceAlert> PriceAlerts => Set<PriceAlert>();
    public DbSet<AiTraderPortfolio> AiTraderPortfolios => Set<AiTraderPortfolio>();
    public DbSet<AiTraderPosition> AiTraderPositions => Set<AiTraderPosition>();
    public DbSet<AiTrade> AiTrades => Set<AiTrade>();
    public DbSet<AiTraderValuationSnapshot> AiTraderValuationSnapshots => Set<AiTraderValuationSnapshot>();
    public DbSet<AiDecision> AiDecisions => Set<AiDecision>();
    public DbSet<AiRejectedDecision> AiRejectedDecisions => Set<AiRejectedDecision>();
    public DbSet<AiExecutionAttempt> AiExecutionAttempts => Set<AiExecutionAttempt>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateValuationHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateValuationHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateValuationHistory()
    {
        if (ChangeTracker.Entries<AiTraderValuationSnapshot>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("AI valuation history is append-only.");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockLabDbContext).Assembly);
        if (Database.IsSqlServer())
        {
            modelBuilder.Entity<User>().Property(user => user.PreferredLanguage).UseCollation("Latin1_General_100_BIN2");
            AiDecisionConfiguration.ConfigureSqlServerSignal(modelBuilder.Entity<AiDecision>());
            AiRejectedDecisionConfiguration.ConfigureSqlServerReason(modelBuilder.Entity<AiRejectedDecision>());
            AiExecutionAttemptConfiguration.ConfigureSqlServerStatus(modelBuilder.Entity<AiExecutionAttempt>());
        }
    }
}
