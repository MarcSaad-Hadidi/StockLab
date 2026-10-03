using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Signal = table.Column<string>(type: "varchar(4)", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(29,28)", precision: 29, scale: 28, nullable: false),
                    DecisionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(128)", nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(128)", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDecisions", x => x.Id);
                    table.CheckConstraint("CK_AiDecisions_Confidence", "[Confidence] >= 0 AND [Confidence] <= 1");
                    table.CheckConstraint("CK_AiDecisions_ModelName_NotBlank", "LTRIM(RTRIM([ModelName])) <> ''");
                    table.CheckConstraint("CK_AiDecisions_ModelVersion_NotBlank", "LTRIM(RTRIM([ModelVersion])) <> ''");
                    table.CheckConstraint("CK_AiDecisions_Signal", "[Signal] COLLATE Latin1_General_100_BIN2 IN ('BUY', 'SELL', 'HOLD') AND DATALENGTH([Signal]) = CASE WHEN [Signal] = 'BUY' THEN 3 ELSE 4 END");
                    table.CheckConstraint("CK_AiDecisions_Symbol_NotBlank", "LTRIM(RTRIM([Symbol])) <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiDecisions_DecisionDate_RecordedAtUtc_Id",
                table: "AiDecisions",
                columns: new[] { "DecisionDate", "RecordedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiDecisions");
        }
    }
}
