using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class PriceAlertConfiguration : IEntityTypeConfiguration<PriceAlert>
{
    public void Configure(EntityTypeBuilder<PriceAlert> builder)
    {
        builder.ToTable("PriceAlerts", table =>
        {
            table.HasCheckConstraint("CK_PriceAlerts_Condition", "[Condition] IN ('Above', 'Below')");
            table.HasCheckConstraint("CK_PriceAlerts_Status", "[Status] IN ('Active', 'Disabled', 'Triggered')");
            table.HasCheckConstraint("CK_PriceAlerts_TargetPrice", "[TargetPrice] > 0");
            table.HasCheckConstraint("CK_PriceAlerts_TriggeredPrice", "[TriggeredPrice] IS NULL OR [TriggeredPrice] > 0");
            table.HasCheckConstraint("CK_PriceAlerts_TriggeredFields",
                "([Status] = 'Triggered' AND [TriggeredPrice] IS NOT NULL AND [TriggeredAtUtc] IS NOT NULL) " +
                "OR ([Status] IN ('Active', 'Disabled') AND [TriggeredPrice] IS NULL AND [TriggeredAtUtc] IS NULL)");
            table.HasCheckConstraint("CK_PriceAlerts_TriggeredAtUtc", "[TriggeredAtUtc] IS NULL OR [TriggeredAtUtc] >= [CreatedAtUtc]");
            table.HasCheckConstraint("CK_PriceAlerts_UpdatedAtUtc", "[UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        builder.HasKey(alert => alert.Id);
        builder.Property(alert => alert.Symbol).HasColumnType("nvarchar(32)").IsRequired();
        builder.Property(alert => alert.Currency).HasColumnType("char(3)").IsRequired().HasDefaultValue("USD");
        builder.Property(alert => alert.Condition).HasColumnType("varchar(5)").IsRequired();
        builder.Property(alert => alert.TargetPrice).HasColumnType("decimal(19,4)");
        builder.Property(alert => alert.Status).HasColumnType("varchar(9)").IsRequired();
        builder.Property(alert => alert.TriggeredPrice).HasColumnType("decimal(19,4)");
        builder.Property(alert => alert.TriggeredAtUtc).HasColumnType("datetime2(7)");
        builder.Property(alert => alert.CreatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(alert => alert.UpdatedAtUtc).HasColumnType("datetime2(7)");
        builder.Property(alert => alert.Version).IsRowVersion().HasColumnType("rowversion");
        builder.HasOne(alert => alert.User)
            .WithMany(user => user.PriceAlerts)
            .HasForeignKey(alert => alert.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(alert => new { alert.UserId, alert.Status });
        builder.HasIndex(alert => new { alert.Symbol, alert.Currency }).HasFilter("[Status] = 'Active'");
    }
}
