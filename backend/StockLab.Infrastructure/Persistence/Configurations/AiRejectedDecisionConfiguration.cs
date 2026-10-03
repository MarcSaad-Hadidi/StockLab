using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Application.DTOs.AiTrader;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class AiRejectedDecisionConfiguration : IEntityTypeConfiguration<AiRejectedDecision>
{
    private static readonly string ReasonSet = string.Join(", ", Enum.GetNames<AiRiskRejectionReason>().Select(name => $"'{name}'"));

    public void Configure(EntityTypeBuilder<AiRejectedDecision> builder)
    {
        builder.ToTable("AiRejectedDecisions", table =>
            table.HasCheckConstraint("CK_AiRejectedDecisions_RejectionReason", $"[RejectionReason] IN ({ReasonSet})"));
        builder.HasKey(r => r.AiDecisionId);
        builder.Property(r => r.AiDecisionId).ValueGeneratedNever();
        builder.Property(r => r.RejectionReason).HasColumnType("varchar(64)").IsRequired();
        builder.Property(r => r.RejectedAtUtc).HasColumnType("datetime2(7)")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.HasOne<AiDecision>().WithOne().HasForeignKey<AiRejectedDecision>(r => r.AiDecisionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(r => new { r.RejectedAtUtc, r.AiDecisionId });
    }

    internal static void ConfigureSqlServerReason(EntityTypeBuilder<AiRejectedDecision> builder)
    {
        // Binary casing and exact byte length defeat SQL Server's case-insensitive, padded comparisons.
        builder.Metadata.RemoveCheckConstraint("CK_AiRejectedDecisions_RejectionReason");
        builder.ToTable("AiRejectedDecisions", table => table.HasCheckConstraint("CK_AiRejectedDecisions_RejectionReason",
            $"[RejectionReason] COLLATE Latin1_General_100_BIN2 IN ({ReasonSet}) AND DATALENGTH([RejectionReason]) = LEN([RejectionReason])"));
    }
}
