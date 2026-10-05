using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockLab.Domain.Entities;

namespace StockLab.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_Side", "[Side] IN ('BUY', 'SELL')");
            table.HasCheckConstraint("CK_Transactions_OrderType", "[OrderType] IN ('market', 'limit')");
            table.HasCheckConstraint("CK_Transactions_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_Transactions_ExecutionPrice", "[ExecutionPrice] > 0");
            table.HasCheckConstraint("CK_Transactions_TotalAmount", "[TotalAmount] > 0");
        });
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Side).HasColumnType("varchar(4)").IsRequired();
        builder.Property(transaction => transaction.Symbol).HasColumnType("nvarchar(32)").IsRequired();
        builder.Property(transaction => transaction.RequestedSymbol).HasColumnType("nvarchar(32)");
        builder.Property(transaction => transaction.Quantity).HasColumnType("decimal(19,8)");
        builder.Property(transaction => transaction.OrderType).HasColumnType("varchar(6)").IsRequired().HasDefaultValue("market");
        builder.Property(transaction => transaction.LimitPrice).HasColumnType("decimal(19,4)");
        builder.Property(transaction => transaction.ExecutionPrice).HasColumnType("decimal(19,4)");
        builder.Property(transaction => transaction.TotalAmount).HasColumnType("decimal(27,12)");
        builder.Property(transaction => transaction.ExecutedAtUtc).HasColumnType("datetime2(7)");
        builder.HasOne(transaction => transaction.Portfolio)
            .WithMany(portfolio => portfolio.Transactions)
            .HasForeignKey(transaction => transaction.PortfolioId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(transaction => new { transaction.PortfolioId, transaction.OrderId }).IsUnique();
        builder.HasIndex(transaction => new { transaction.PortfolioId, transaction.ExecutedAtUtc, transaction.Id });
        builder.HasIndex(transaction => new { transaction.PortfolioId, transaction.Symbol, transaction.ExecutedAtUtc });
        builder.HasIndex(transaction => new { transaction.PortfolioId, transaction.Side, transaction.ExecutedAtUtc });
    }
}
