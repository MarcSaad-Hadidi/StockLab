using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiRiskExecutionAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiExecutionAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    Status = table.Column<string>(type: "varchar(12)", nullable: false),
                    FailureCode = table.Column<string>(type: "varchar(64)", nullable: true),
                    RequestedPrice = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(19,8)", nullable: true),
                    TradeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsReplay = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiExecutionAttempts", x => x.Id);
                    table.CheckConstraint("CK_AiExecutionAttempts_Approval", "([RequestedPrice] IS NULL AND [ApprovedQuantity] IS NULL) OR ([RequestedPrice] IS NOT NULL AND [ApprovedQuantity] IS NOT NULL AND [RequestedPrice] > 0 AND [ApprovedQuantity] >= 0)");
                    table.CheckConstraint("CK_AiExecutionAttempts_Completion", "([Status] = 'STARTED' AND [CompletedAtUtc] IS NULL) OR ([Status] <> 'STARTED' AND [CompletedAtUtc] IS NOT NULL AND [CompletedAtUtc] >= [StartedAtUtc])");
                    table.CheckConstraint("CK_AiExecutionAttempts_Outcome", "([Status] = 'EXECUTED' AND [TradeId] IS NOT NULL AND [FailureCode] IS NULL) OR ([Status] = 'STARTED' AND [TradeId] IS NULL AND [FailureCode] IS NULL) OR ([Status] IN ('REJECTED', 'FAILED', 'CANCELLED') AND [TradeId] IS NULL AND [FailureCode] IS NOT NULL)");
                    table.CheckConstraint("CK_AiExecutionAttempts_Status", "[Status] COLLATE Latin1_General_100_BIN2 IN ('STARTED', 'EXECUTED', 'REJECTED', 'FAILED', 'CANCELLED') AND DATALENGTH([Status]) = LEN([Status])");
                    table.ForeignKey(
                        name: "FK_AiExecutionAttempts_AiDecisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "AiDecisions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AiExecutionAttempts_AiTrades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "AiTrades",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionAttempts_DecisionId_StartedAtUtc_Id",
                table: "AiExecutionAttempts",
                columns: new[] { "DecisionId", "StartedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutionAttempts_TradeId",
                table: "AiExecutionAttempts",
                column: "TradeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiExecutionAttempts");
        }
    }
}
