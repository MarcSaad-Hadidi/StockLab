using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class AiTraderPositionConfiguration : IEntityTypeConfiguration<AiTraderPosition>
{
    public void Configure(EntityTypeBuilder<AiTraderPosition> builder)
    {
        builder.ToTable("AiPositions", table =>
        {
            table.HasCheckConstraint("CK_AiPositions_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_AiPositions_AverageCost", "[AverageCost] > 0");
        });
        builder.HasKey(position => position.Id);
        builder.Property(position => position.AiTraderPortfolioId).HasColumnName("AiPortfolioId");
        builder.Property(position => position.Symbol).HasColumnType("nvarchar(32)").IsRequired();
        builder.Property(position => position.Quantity).HasColumnType("decimal(19,8)");
        builder.Property(position => position.AverageCost).HasColumnType("decimal(19,4)");
        builder.Property(position => position.CreatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(position => position.UpdatedAtUtc).HasColumnType("datetime2(7)");
        builder.HasOne(position => position.Portfolio)
            .WithMany(portfolio => portfolio.Positions)
            .HasForeignKey(position => position.AiTraderPortfolioId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(position => new { position.AiTraderPortfolioId, position.Symbol }).IsUnique();
    }
}
