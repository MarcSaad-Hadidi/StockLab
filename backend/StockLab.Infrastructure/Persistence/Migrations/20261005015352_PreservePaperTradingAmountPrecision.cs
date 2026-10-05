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
