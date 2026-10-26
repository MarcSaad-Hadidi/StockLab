using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class AiExecutionAttemptConfiguration : IEntityTypeConfiguration<AiExecutionAttempt>
{
    private const string Statuses = "'STARTED', 'EXECUTED', 'REJECTED', 'FAILED', 'CANCELLED'";
    public void Configure(EntityTypeBuilder<AiExecutionAttempt> builder)
    {
        builder.ToTable("AiExecutionAttempts", table =>
        {
            table.HasCheckConstraint("CK_AiExecutionAttempts_Status", $"[Status] IN ({Statuses})");
            table.HasCheckConstraint("CK_AiExecutionAttempts_Completion", "([Status] = 'STARTED' AND [CompletedAtUtc] IS NULL) OR ([Status] <> 'STARTED' AND [CompletedAtUtc] IS NOT NULL AND [CompletedAtUtc] >= [StartedAtUtc])");
            table.HasCheckConstraint("CK_AiExecutionAttempts_Outcome", "([Status] = 'EXECUTED' AND [TradeId] IS NOT NULL AND [FailureCode] IS NULL) OR ([Status] = 'STARTED' AND [TradeId] IS NULL AND [FailureCode] IS NULL) OR ([Status] IN ('REJECTED', 'FAILED', 'CANCELLED') AND [TradeId] IS NULL AND [FailureCode] IS NOT NULL)");
            table.HasCheckConstraint("CK_AiExecutionAttempts_Approval", "([RequestedPrice] IS NULL AND [ApprovedQuantity] IS NULL) OR ([RequestedPrice] IS NOT NULL AND [ApprovedQuantity] IS NOT NULL AND [RequestedPrice] > 0 AND [ApprovedQuantity] >= 0)");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasColumnType("varchar(12)").IsRequired();
        builder.Property(a => a.FailureCode).HasColumnType("varchar(64)");
        builder.Property(a => a.RequestedPrice).HasColumnType("decimal(19,4)");
        builder.Property(a => a.ApprovedQuantity).HasColumnType("decimal(19,8)");
        builder.Property(a => a.StartedAtUtc).HasColumnType("datetime2(7)")
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(a => a.CompletedAtUtc).HasColumnType("datetime2(7)")
            .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
        builder.HasOne<AiDecision>().WithMany().HasForeignKey(a => a.DecisionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AiTrade>().WithMany().HasForeignKey(a => a.TradeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(a => new { a.DecisionId, a.StartedAtUtc, a.Id });
    }

    internal static void ConfigureSqlServerStatus(EntityTypeBuilder<AiExecutionAttempt> builder)
    {
        builder.Metadata.RemoveCheckConstraint("CK_AiExecutionAttempts_Status");
        builder.ToTable("AiExecutionAttempts", table => table.HasCheckConstraint("CK_AiExecutionAttempts_Status",
            $"[Status] COLLATE Latin1_General_100_BIN2 IN ({Statuses}) AND DATALENGTH([Status]) = LEN([Status])"));
    }
}
