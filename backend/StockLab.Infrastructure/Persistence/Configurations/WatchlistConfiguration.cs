using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class WatchlistConfiguration : IEntityTypeConfiguration<Watchlist>
{
    public void Configure(EntityTypeBuilder<Watchlist> builder)
    {
        builder.ToTable("Watchlists");
        builder.HasKey(watchlist => watchlist.Id);
        builder.Property(watchlist => watchlist.Symbol).HasColumnType("nvarchar(32)").IsRequired();
        builder.Property(watchlist => watchlist.CreatedAtUtc).HasColumnType("datetime2(7)");
        builder.HasOne(watchlist => watchlist.User)
            .WithMany(user => user.Watchlists)
            .HasForeignKey(watchlist => watchlist.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(watchlist => new { watchlist.UserId, watchlist.Symbol }).IsUnique();
    }
}
