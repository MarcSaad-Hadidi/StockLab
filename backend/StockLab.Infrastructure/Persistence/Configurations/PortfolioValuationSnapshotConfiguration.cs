using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Globalization;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class PortfolioValuationSnapshotConfiguration : IEntityTypeConfiguration<PortfolioValuationSnapshot>
{
    public void Configure(EntityTypeBuilder<PortfolioValuationSnapshot> builder)
    {
        builder.ToTable("PortfolioValuationSnapshots", table =>
        {
            table.HasCheckConstraint("CK_PortfolioValuationSnapshots_Amounts", "CAST([CashBalance] AS decimal(38,12)) >= 0 AND CAST([PositionsValue] AS decimal(38,12)) >= 0 AND CAST([TotalValue] AS decimal(38,12)) >= 0 AND [InitialCapital] > 0 AND LTRIM(RTRIM([CashBalance])) <> '' AND LTRIM(RTRIM([PositionsValue])) <> '' AND LTRIM(RTRIM([TotalValue])) <> ''");
            table.HasCheckConstraint("CK_PortfolioValuationSnapshots_Times", "[SlotUtc] > '0001-01-01' AND [RecordedAtUtc] >= [SlotUtc]");
            table.HasCheckConstraint("CK_PortfolioValuationSnapshots_Source", "[Source] IN ('Cash', 'Mock', 'TwelveData')");
        });
        builder.HasKey(point => point.Id);
        builder.HasOne<Portfolio>().WithMany().HasForeignKey(point => point.PortfolioId).OnDelete(DeleteBehavior.NoAction);
        // This immutable read model does no SQL monetary arithmetic. Canonical decimal text
        // preserves wide derived valuations without SqlClient coercing 38 digits through System.Decimal.
        foreach (var property in new[] { nameof(PortfolioValuationSnapshot.CashBalance), nameof(PortfolioValuationSnapshot.PositionsValue), nameof(PortfolioValuationSnapshot.TotalValue) })
            builder.Property<decimal>(property).HasColumnType("varchar(64)").HasConversion(
                value => value.ToString("0.############", CultureInfo.InvariantCulture),
                value => decimal.Parse(value, CultureInfo.InvariantCulture));
        builder.Property(point => point.InitialCapital).HasColumnType("decimal(19,4)");
        builder.Property(point => point.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(point => point.Source).HasMaxLength(20).IsRequired();
        builder.Property(point => point.PortfolioVersion).HasColumnType("varbinary(8)").IsRequired();
        builder.Property(point => point.QuoteObservationsJson).IsRequired();
        builder.Property(point => point.SlotUtc).HasColumnType("datetime2(7)")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(point => point.RecordedAtUtc).HasColumnType("datetime2(7)")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(point => point.QuoteAsOfUtc).HasColumnType("datetime2(7)")
            .HasConversion(value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : (DateTime?)null);
        builder.HasIndex(point => new { point.PortfolioId, point.SlotUtc }).IsUnique();
        builder.HasIndex(point => new { point.PortfolioId, point.RecordedAtUtc, point.Id });
    }
}
