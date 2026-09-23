using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class PortfolioConfiguration : IEntityTypeConfiguration<Portfolio>
{
    public void Configure(EntityTypeBuilder<Portfolio> builder)
    {
        builder.ToTable("Portfolios", table =>
        {
            table.HasCheckConstraint("CK_Portfolios_InitialCapital", "[InitialCapital] > 0");
            table.HasCheckConstraint("CK_Portfolios_CashBalance", "[CashBalance] >= 0");
        });
        builder.HasKey(portfolio => portfolio.Id);
        builder.Property(portfolio => portfolio.Currency).HasColumnType("char(3)").IsRequired().HasDefaultValue("USD");
        builder.Property(portfolio => portfolio.InitialCapital).HasColumnType("decimal(19,4)").HasDefaultValue(100000m);
        builder.Property(portfolio => portfolio.CashBalance).HasColumnType("decimal(19,4)").HasDefaultValue(100000m);
        builder.Property(portfolio => portfolio.CreatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(portfolio => portfolio.Version).IsRowVersion().HasColumnType("rowversion");
        builder.HasOne(portfolio => portfolio.User)
            .WithOne(user => user.Portfolio)
            .HasForeignKey<Portfolio>(portfolio => portfolio.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(portfolio => portfolio.UserId).IsUnique();
    }
}
