using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperTradeRequestedSymbol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy symbols need provider-verified exchange metadata. PaperTradingEngine
            // reconciles their positions and order identifiers atomically on the next trade.
            migrationBuilder.AddColumn<string>(
                name: "RequestedSymbol",
                table: "Transactions",
                type: "nvarchar(32)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedSymbol",
                table: "Transactions");
        }
    }
}
