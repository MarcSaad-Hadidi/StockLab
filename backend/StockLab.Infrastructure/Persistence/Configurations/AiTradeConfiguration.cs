using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class AiTradeConfiguration : IEntityTypeConfiguration<AiTrade>
{
    public void Configure(EntityTypeBuilder<AiTrade> builder)
    {
        builder.ToTable("AiTrades", table =>
        {
            table.HasCheckConstraint("CK_AiTrades_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_AiTrades_ExecutionPrice", "[ExecutionPrice] > 0");
            table.HasCheckConstraint("CK_AiTrades_TotalAmount", "[TotalAmount] > 0");
            table.HasCheckConstraint("CK_AiTrades_Side", "[Side] IN ('BUY', 'SELL')");
        });
        builder.HasKey(trade => trade.Id);
        builder.Property(trade => trade.Side).HasColumnType("nvarchar(4)").IsRequired();
        builder.Property(trade => trade.Symbol).HasColumnType("nvarchar(32)").IsRequired();
        builder.Property(trade => trade.OrderFingerprint).HasColumnType("char(64)").IsRequired();
        builder.Property(trade => trade.Quantity).HasColumnType("decimal(19,8)");
        builder.Property(trade => trade.ExecutionPrice).HasColumnType("decimal(19,4)");
        builder.Property(trade => trade.TotalAmount).HasColumnType("decimal(19,4)");
        builder.Property(trade => trade.ExecutedAtUtc).HasColumnType("datetime2(7)")
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(trade => trade.CashBalanceAfter).HasColumnType("decimal(19,4)");
        builder.Property(trade => trade.PositionQuantityAfter).HasColumnType("decimal(19,8)");
        builder.Property(trade => trade.AverageCostAfter).HasColumnType("decimal(19,4)");
        builder.HasOne(trade => trade.Portfolio).WithMany()
            .HasForeignKey(trade => trade.AiTraderPortfolioId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(trade => new { trade.AiTraderPortfolioId, trade.OrderId }).IsUnique();
        builder.HasOne(trade => trade.Decision).WithMany()
            .HasForeignKey(trade => trade.AiDecisionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(trade => trade.AiDecisionId).IsUnique().HasFilter("[AiDecisionId] IS NOT NULL");
        builder.HasIndex(trade => new { trade.ExecutedAtUtc, trade.Id });
    }
}
