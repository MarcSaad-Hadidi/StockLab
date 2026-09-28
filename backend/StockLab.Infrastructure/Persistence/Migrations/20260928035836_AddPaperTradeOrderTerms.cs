using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperTradeOrderTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LimitPrice",
                table: "Transactions",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderType",
                table: "Transactions",
                type: "varchar(6)",
                nullable: false,
                defaultValue: "market");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_OrderType",
                table: "Transactions",
                sql: "[OrderType] IN ('market', 'limit')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_OrderType",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LimitPrice",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OrderType",
                table: "Transactions");
        }
    }
}
