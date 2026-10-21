using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class AiTraderValuationSnapshotConfiguration : IEntityTypeConfiguration<AiTraderValuationSnapshot>
{
    public void Configure(EntityTypeBuilder<AiTraderValuationSnapshot> builder)
    {
        builder.ToTable("AiPortfolioSnapshots", table =>
        {
            table.HasCheckConstraint("CK_AiPortfolioSnapshots_Cash", "[CashBalance] >= 0");
            table.HasCheckConstraint("CK_AiPortfolioSnapshots_Positions", "[PositionsValue] >= 0");
            table.HasCheckConstraint("CK_AiPortfolioSnapshots_Currency", "[Currency] = 'USD'");
            table.HasCheckConstraint("CK_AiPortfolioSnapshots_Timestamp", "[ValuedAtUtc] > '0001-01-01T00:00:00'");
            table.HasCheckConstraint("CK_AiPortfolioSnapshots_CaptureId", "[CaptureId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AiPortfolioSnapshots_Baseline", "[IsInitialBaseline] = 0 OR ([CashBalance] = 100000 AND [PositionsValue] = 0 AND [QuoteObservationsJson] = '[]')");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.AiTraderPortfolioId).HasColumnName("AiPortfolioId");
        builder.Property(p => p.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(p => p.CashBalance).HasColumnType("decimal(19,4)");
        builder.Property(p => p.PositionsValue).HasColumnType("decimal(19,4)");
        builder.Property(p => p.ValuedAtUtc).HasColumnType("datetime2(7)")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(p => p.PortfolioVersion).HasColumnType("varbinary(8)").IsRequired();
        builder.Property(p => p.QuoteObservationsJson).IsRequired();
        builder.HasOne<AiTraderPortfolio>().WithMany().HasForeignKey(p => p.AiTraderPortfolioId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(p => p.CaptureId).IsUnique();
        builder.HasIndex(p => new { p.AiTraderPortfolioId, p.ValuedAtUtc, p.Id });
        builder.HasIndex(p => p.AiTraderPortfolioId).IsUnique().HasFilter("[IsInitialBaseline] = 1");
    }
}
