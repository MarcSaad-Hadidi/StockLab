using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkAiTradesToDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiDecisionId",
                table: "AiTrades",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiTrades_AiDecisionId",
                table: "AiTrades",
                column: "AiDecisionId",
                unique: true,
                filter: "[AiDecisionId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AiTrades_AiDecisions_AiDecisionId",
                table: "AiTrades",
                column: "AiDecisionId",
                principalTable: "AiDecisions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiTrades_AiDecisions_AiDecisionId",
                table: "AiTrades");

            migrationBuilder.DropIndex(
                name: "IX_AiTrades_AiDecisionId",
                table: "AiTrades");

            migrationBuilder.DropColumn(
                name: "AiDecisionId",
                table: "AiTrades");
        }
    }
}
