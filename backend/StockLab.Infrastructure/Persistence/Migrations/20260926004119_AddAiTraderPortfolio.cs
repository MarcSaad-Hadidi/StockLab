using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiTraderPortfolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiTraderPortfolios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortfolioKey = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    InitialCapital = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CashBalance = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTraderPortfolios", x => x.Id);
                    table.CheckConstraint("CK_AiTraderPortfolios_CashBalance", "[CashBalance] >= 0");
                    table.CheckConstraint("CK_AiTraderPortfolios_Currency", "[Currency] = 'USD'");
                    table.CheckConstraint("CK_AiTraderPortfolios_InitialCapital", "[InitialCapital] = 100000");
                });

            migrationBuilder.CreateTable(
                name: "AiTraderPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AiTraderPortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                    AverageCost = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTraderPositions", x => x.Id);
                    table.CheckConstraint("CK_AiTraderPositions_AverageCost", "[AverageCost] > 0");
                    table.CheckConstraint("CK_AiTraderPositions_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_AiTraderPositions_AiTraderPortfolios_AiTraderPortfolioId",
                        column: x => x.AiTraderPortfolioId,
                        principalTable: "AiTraderPortfolios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiTraderPortfolios_PortfolioKey",
                table: "AiTraderPortfolios",
                column: "PortfolioKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiTraderPositions_AiTraderPortfolioId_Symbol",
                table: "AiTraderPositions",
                columns: new[] { "AiTraderPortfolioId", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiTraderPositions");

            migrationBuilder.DropTable(
                name: "AiTraderPortfolios");
        }
    }
}
