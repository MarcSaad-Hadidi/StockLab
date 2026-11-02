using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalPortfolioPerformanceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioValuationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CashBalance = table.Column<string>(type: "varchar(64)", nullable: false),
                    PositionsValue = table.Column<string>(type: "varchar(64)", nullable: false),
                    TotalValue = table.Column<string>(type: "varchar(64)", nullable: false),
                    InitialCapital = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QuoteAsOfUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    PortfolioVersion = table.Column<byte[]>(type: "varbinary(8)", nullable: false),
                    QuoteObservationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioValuationSnapshots", x => x.Id);
                    table.CheckConstraint("CK_PortfolioValuationSnapshots_Amounts", "CAST([CashBalance] AS decimal(38,12)) >= 0 AND CAST([PositionsValue] AS decimal(38,12)) >= 0 AND CAST([TotalValue] AS decimal(38,12)) >= 0 AND [InitialCapital] > 0 AND LTRIM(RTRIM([CashBalance])) <> '' AND LTRIM(RTRIM([PositionsValue])) <> '' AND LTRIM(RTRIM([TotalValue])) <> ''");
                    table.CheckConstraint("CK_PortfolioValuationSnapshots_Source", "[Source] IN ('Cash', 'Mock', 'TwelveData')");
                    table.CheckConstraint("CK_PortfolioValuationSnapshots_Times", "[SlotUtc] > '0001-01-01' AND [RecordedAtUtc] >= [SlotUtc]");
                    table.ForeignKey(
                        name: "FK_PortfolioValuationSnapshots_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioValuationSnapshots_PortfolioId_RecordedAtUtc_Id",
                table: "PortfolioValuationSnapshots",
                columns: new[] { "PortfolioId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioValuationSnapshots_PortfolioId_SlotUtc",
                table: "PortfolioValuationSnapshots",
                columns: new[] { "PortfolioId", "SlotUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [PortfolioValuationSnapshots])
                BEGIN
                    THROW 51002, N'Cannot roll back personal performance history while recorded observations exist. Preserve the history table.', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "PortfolioValuationSnapshots");
        }
    }
}
