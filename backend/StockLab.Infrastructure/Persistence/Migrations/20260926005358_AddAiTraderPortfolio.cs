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
                name: "AiPortfolios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    InitialCapital = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CashBalance = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPortfolios", x => x.Id);
                    table.CheckConstraint("CK_AiPortfolios_CashBalance", "[CashBalance] >= 0");
                    table.CheckConstraint("CK_AiPortfolios_Currency", "[Currency] = 'USD'");
                    table.CheckConstraint("CK_AiPortfolios_InitialCapital", "[InitialCapital] = 100000");
                    table.CheckConstraint("CK_AiPortfolios_Name_NotBlank", "LTRIM(RTRIM([Name])) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "AiPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AiPortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                    AverageCost = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPositions", x => x.Id);
                    table.CheckConstraint("CK_AiPositions_AverageCost", "[AverageCost] > 0");
                    table.CheckConstraint("CK_AiPositions_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_AiPositions_AiPortfolios_AiPortfolioId",
                        column: x => x.AiPortfolioId,
                        principalTable: "AiPortfolios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPortfolios_Name",
                table: "AiPortfolios",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiPositions_AiPortfolioId_Symbol",
                table: "AiPositions",
                columns: new[] { "AiPortfolioId", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiPositions");

            migrationBuilder.DropTable(
                name: "AiPortfolios");
        }
    }
}
