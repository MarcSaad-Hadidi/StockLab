using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreservePaperTradingAmountPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TotalAmount",
                table: "Transactions",
                type: "decimal(27,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "CashBalance",
                table: "Portfolios",
                type: "decimal(27,12)",
                nullable: false,
                defaultValue: 100000m,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)",
                oldDefaultValue: 100000m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse lossy rollback: the previous engine cannot preserve twelve-decimal data.
            // Throw before any DDL or migration-history removal if an amount cannot fit.
            // Hold both tables against concurrent writes until the migration commits.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [Portfolios] WITH (TABLOCKX, HOLDLOCK)
                    WHERE TRY_CONVERT(decimal(19,4), [CashBalance]) IS NULL
                       OR [CashBalance] <> TRY_CONVERT(decimal(19,4), [CashBalance]))
                OR EXISTS (
                    SELECT 1 FROM [Transactions] WITH (TABLOCKX, HOLDLOCK)
                    WHERE TRY_CONVERT(decimal(19,4), [TotalAmount]) IS NULL
                       OR [TotalAmount] <> TRY_CONVERT(decimal(19,4), [TotalAmount]))
                BEGIN
                    ;THROW 51021, N'Cannot roll back PreservePaperTradingAmountPrecision: cash balances or transaction totals cannot be represented exactly as decimal(19,4). Keep the precision migration and current application version.', 1;
                END
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalAmount",
                table: "Transactions",
                type: "decimal(19,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(27,12)");

            migrationBuilder.AlterColumn<decimal>(
                name: "CashBalance",
                table: "Portfolios",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 100000m,
                oldClrType: typeof(decimal),
                oldType: "decimal(27,12)",
                oldDefaultValue: 100000m);
        }
    }
}
