using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiTraderPerformanceMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RealizedPnL",
                table: "AiTrades",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiPortfolioSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AiPortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaptureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValuedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CashBalance = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    PositionsValue = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    IsInitialBaseline = table.Column<bool>(type: "bit", nullable: false),
                    PortfolioVersion = table.Column<byte[]>(type: "varbinary(8)", nullable: false),
                    QuoteObservationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPortfolioSnapshots", x => x.Id);
                    table.CheckConstraint("CK_AiPortfolioSnapshots_Baseline", "[IsInitialBaseline] = 0 OR ([CashBalance] = 100000 AND [PositionsValue] = 0 AND [QuoteObservationsJson] = '[]')");
                    table.CheckConstraint("CK_AiPortfolioSnapshots_CaptureId", "[CaptureId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AiPortfolioSnapshots_Cash", "[CashBalance] >= 0");
                    table.CheckConstraint("CK_AiPortfolioSnapshots_Currency", "[Currency] = 'USD'");
                    table.CheckConstraint("CK_AiPortfolioSnapshots_Positions", "[PositionsValue] >= 0");
                    table.CheckConstraint("CK_AiPortfolioSnapshots_Timestamp", "[ValuedAtUtc] > '0001-01-01T00:00:00'");
                    table.ForeignKey(
                        name: "FK_AiPortfolioSnapshots_AiPortfolios_AiPortfolioId",
                        column: x => x.AiPortfolioId,
                        principalTable: "AiPortfolios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPortfolioSnapshots_AiPortfolioId",
                table: "AiPortfolioSnapshots",
                column: "AiPortfolioId",
                unique: true,
                filter: "[IsInitialBaseline] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AiPortfolioSnapshots_AiPortfolioId_ValuedAtUtc_Id",
                table: "AiPortfolioSnapshots",
                columns: new[] { "AiPortfolioId", "ValuedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AiPortfolioSnapshots_CaptureId",
                table: "AiPortfolioSnapshots",
                column: "CaptureId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiPortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "RealizedPnL",
                table: "AiTrades");
        }
    }
}
