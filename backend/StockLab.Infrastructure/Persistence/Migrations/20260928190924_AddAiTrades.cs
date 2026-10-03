using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiTrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AiTraderPortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderFingerprint = table.Column<string>(type: "char(64)", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(4)", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                    ExecutionPrice = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CashBalanceAfter = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    PositionQuantityAfter = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                    AverageCostAfter = table.Column<decimal>(type: "decimal(19,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTrades", x => x.Id);
                    table.CheckConstraint("CK_AiTrades_ExecutionPrice", "[ExecutionPrice] > 0");
                    table.CheckConstraint("CK_AiTrades_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_AiTrades_Side", "[Side] IN ('BUY', 'SELL')");
                    table.CheckConstraint("CK_AiTrades_TotalAmount", "[TotalAmount] > 0");
                    table.ForeignKey(
                        name: "FK_AiTrades_AiPortfolios_AiTraderPortfolioId",
                        column: x => x.AiTraderPortfolioId,
                        principalTable: "AiPortfolios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiTrades_AiTraderPortfolioId_OrderId",
                table: "AiTrades",
                columns: new[] { "AiTraderPortfolioId", "OrderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiTrades");
        }
    }
}
