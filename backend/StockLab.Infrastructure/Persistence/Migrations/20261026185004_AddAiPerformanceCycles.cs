using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPerformanceCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CycleSlotUtc",
                table: "AiPortfolioSnapshots",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeContextJson",
                table: "AiPortfolioSnapshots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiPortfolioSnapshots_AiPortfolioId_CycleSlotUtc",
                table: "AiPortfolioSnapshots",
                columns: new[] { "AiPortfolioId", "CycleSlotUtc" },
                unique: true,
                filter: "[CycleSlotUtc] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiPortfolioSnapshots_Cycle",
                table: "AiPortfolioSnapshots",
                sql: "([CycleSlotUtc] IS NULL AND [TradeContextJson] IS NULL) OR ([CycleSlotUtc] IS NOT NULL AND [TradeContextJson] IS NOT NULL AND [IsInitialBaseline] = 0 AND [CycleSlotUtc] > '0001-01-01T00:00:00' AND [CycleSlotUtc] <= [ValuedAtUtc])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiPortfolioSnapshots_AiPortfolioId_CycleSlotUtc",
                table: "AiPortfolioSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiPortfolioSnapshots_Cycle",
                table: "AiPortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "CycleSlotUtc",
                table: "AiPortfolioSnapshots");

            migrationBuilder.DropColumn(
                name: "TradeContextJson",
                table: "AiPortfolioSnapshots");
        }
    }
}
