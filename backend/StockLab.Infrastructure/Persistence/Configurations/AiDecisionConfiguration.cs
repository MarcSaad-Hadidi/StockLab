using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class AiDecisionConfiguration : IEntityTypeConfiguration<AiDecision>
{
    public void Configure(EntityTypeBuilder<AiDecision> builder)
    {
        builder.ToTable("AiDecisions", table =>
        {
            table.HasCheckConstraint("CK_AiDecisions_Signal", "[Signal] IN ('BUY', 'SELL', 'HOLD')");
            table.HasCheckConstraint("CK_AiDecisions_Confidence", "[Confidence] >= 0 AND [Confidence] <= 1");
            table.HasCheckConstraint("CK_AiDecisions_Symbol_NotBlank", "LTRIM(RTRIM([Symbol])) <> ''");
            table.HasCheckConstraint("CK_AiDecisions_ModelName_NotBlank", "LTRIM(RTRIM([ModelName])) <> ''");
            table.HasCheckConstraint("CK_AiDecisions_ModelVersion_NotBlank", "LTRIM(RTRIM([ModelVersion])) <> ''");
        });
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Symbol).HasColumnType("nvarchar(32)").IsRequired();
        builder.Property(d => d.Signal).HasColumnType("varchar(4)").IsRequired();
        // Preserve every .NET decimal in [0,1], including unrounded ML confidence scores.
        builder.Property(d => d.Confidence).HasColumnType("decimal(29,28)").HasPrecision(29, 28);
        builder.Property(d => d.DecisionDate).HasColumnType("date");
        builder.Property(d => d.ModelName).HasColumnType("nvarchar(128)").IsRequired();
        builder.Property(d => d.ModelVersion).HasColumnType("nvarchar(128)").IsRequired();
        builder.Property(d => d.RecordedAtUtc).HasColumnType("datetime2(7)")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.HasIndex(d => new { d.DecisionDate, d.RecordedAtUtc, d.Id });
    }

    internal static void ConfigureSqlServerSignal(EntityTypeBuilder<AiDecision> builder)
    {
        // SQL Server's default collation and padded comparisons otherwise accept unreadable 'buy'/'BUY ' rows.
        builder.Metadata.RemoveCheckConstraint("CK_AiDecisions_Signal");
        builder.ToTable("AiDecisions", table => table.HasCheckConstraint("CK_AiDecisions_Signal",
            "[Signal] COLLATE Latin1_General_100_BIN2 IN ('BUY', 'SELL', 'HOLD') " +
            "AND DATALENGTH([Signal]) = CASE WHEN [Signal] = 'BUY' THEN 3 ELSE 4 END"));
    }
}
