using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiRejectedDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiRejectedDecisions",
                columns: table => new
                {
                    AiDecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RejectionReason = table.Column<string>(type: "varchar(64)", nullable: false),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiRejectedDecisions", x => x.AiDecisionId);
                    table.CheckConstraint("CK_AiRejectedDecisions_RejectionReason", "[RejectionReason] COLLATE Latin1_General_100_BIN2 IN ('InvalidDecision', 'InvalidPrice', 'HoldSignal', 'LowConfidence', 'CurrencyMismatch', 'MaxPositionsReached', 'MaxSymbolExposureReached', 'InsufficientCash', 'NoPositionToSell', 'TradeTooSmall') AND DATALENGTH([RejectionReason]) = LEN([RejectionReason])");
                    table.ForeignKey(
                        name: "FK_AiRejectedDecisions_AiDecisions_AiDecisionId",
                        column: x => x.AiDecisionId,
                        principalTable: "AiDecisions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiRejectedDecisions_RejectedAtUtc_AiDecisionId",
                table: "AiRejectedDecisions",
                columns: new[] { "RejectedAtUtc", "AiDecisionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiRejectedDecisions");
        }
    }
}
