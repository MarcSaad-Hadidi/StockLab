using Microsoft.EntityFrameworkCore;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence;

public class StockLabDbContext(DbContextOptions<StockLabDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<PriceAlert> PriceAlerts => Set<PriceAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockLabDbContext).Assembly);
    }
}
