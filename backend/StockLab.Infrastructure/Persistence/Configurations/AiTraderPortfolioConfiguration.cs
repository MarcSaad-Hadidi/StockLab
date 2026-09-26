using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class AiTraderPortfolioConfiguration : IEntityTypeConfiguration<AiTraderPortfolio>
{
    public void Configure(EntityTypeBuilder<AiTraderPortfolio> builder)
    {
        builder.ToTable("AiPortfolios", table =>
        {
            table.HasCheckConstraint("CK_AiPortfolios_Name_NotBlank", "LTRIM(RTRIM([Name])) <> ''");
            table.HasCheckConstraint("CK_AiPortfolios_InitialCapital", "[InitialCapital] = 100000");
            table.HasCheckConstraint("CK_AiPortfolios_CashBalance", "[CashBalance] >= 0");
            table.HasCheckConstraint("CK_AiPortfolios_Currency", "[Currency] = 'USD'");
        });
        builder.HasKey(portfolio => portfolio.Id);
        builder.Property(portfolio => portfolio.PortfolioKey).HasColumnName("Name").HasColumnType("nvarchar(100)").IsRequired();
        builder.HasIndex(portfolio => portfolio.PortfolioKey).IsUnique();
        builder.Property(portfolio => portfolio.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(portfolio => portfolio.InitialCapital).HasColumnType("decimal(19,4)");
        builder.Property(portfolio => portfolio.CashBalance).HasColumnType("decimal(19,4)");
        builder.Property(portfolio => portfolio.CreatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(portfolio => portfolio.UpdatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(portfolio => portfolio.Version).IsRowVersion().HasColumnType("rowversion");
    }
}
