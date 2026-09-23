using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.ToTable("Holdings", table =>
        {
            table.HasCheckConstraint("CK_Holdings_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_Holdings_AverageCost", "[AverageCost] > 0");
        });
        builder.HasKey(holding => holding.Id);
        builder.Property(holding => holding.Symbol).HasColumnType("nvarchar(32)").IsRequired();
        builder.Property(holding => holding.Quantity).HasColumnType("decimal(19,8)");
        builder.Property(holding => holding.AverageCost).HasColumnType("decimal(19,4)");
        builder.Property(holding => holding.UpdatedAtUtc).HasColumnType("datetime2(7)");
        builder.HasOne(holding => holding.Portfolio)
            .WithMany(portfolio => portfolio.Holdings)
            .HasForeignKey(holding => holding.PortfolioId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(holding => new { holding.PortfolioId, holding.Symbol }).IsUnique();
    }
}
