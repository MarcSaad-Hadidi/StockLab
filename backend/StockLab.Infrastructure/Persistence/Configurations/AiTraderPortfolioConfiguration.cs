using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class AiTraderPortfolioConfiguration : IEntityTypeConfiguration<AiTraderPortfolio>
{
    public void Configure(EntityTypeBuilder<AiTraderPortfolio> builder)
    {
        builder.ToTable("AiTraderPortfolios", table =>
        {
            table.HasCheckConstraint("CK_AiTraderPortfolios_InitialCapital", "[InitialCapital] = 100000");
            table.HasCheckConstraint("CK_AiTraderPortfolios_CashBalance", "[CashBalance] >= 0");
            table.HasCheckConstraint("CK_AiTraderPortfolios_Currency", "[Currency] = 'USD'");
        });
        builder.HasKey(portfolio => portfolio.Id);
        builder.Property(portfolio => portfolio.PortfolioKey).HasColumnType("nvarchar(32)").IsRequired();
        builder.HasIndex(portfolio => portfolio.PortfolioKey).IsUnique();
        builder.Property(portfolio => portfolio.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(portfolio => portfolio.InitialCapital).HasColumnType("decimal(19,4)");
        builder.Property(portfolio => portfolio.CashBalance).HasColumnType("decimal(19,4)");
        builder.Property(portfolio => portfolio.CreatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(portfolio => portfolio.UpdatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(portfolio => portfolio.Version).IsRowVersion().HasColumnType("rowversion");
    }
}
