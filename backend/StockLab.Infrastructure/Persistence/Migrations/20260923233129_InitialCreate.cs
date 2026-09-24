using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_DisplayName_NotBlank", "LTRIM(RTRIM([DisplayName])) <> ''");
                    table.CheckConstraint("CK_Users_Email_NotBlank", "LTRIM(RTRIM([Email])) <> ''");
                    table.CheckConstraint("CK_Users_NormalizedEmail_NotBlank", "LTRIM(RTRIM([NormalizedEmail])) <> ''");
                    table.CheckConstraint("CK_Users_PasswordHash_NotBlank", "LTRIM(RTRIM([PasswordHash])) <> ''");
                    table.CheckConstraint("CK_Users_UpdatedAtUtc", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                });

            migrationBuilder.CreateTable(
                name: "Portfolios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "USD"),
                    InitialCapital = table.Column<decimal>(type: "decimal(19,4)", nullable: false, defaultValue: 100000m),
                    CashBalance = table.Column<decimal>(type: "decimal(19,4)", nullable: false, defaultValue: 100000m),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portfolios", x => x.Id);
                    table.CheckConstraint("CK_Portfolios_CashBalance", "[CashBalance] >= 0");
                    table.CheckConstraint("CK_Portfolios_InitialCapital", "[InitialCapital] > 0");
                    table.ForeignKey(
                        name: "FK_Portfolios_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PriceAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "USD"),
                    Condition = table.Column<string>(type: "varchar(5)", nullable: false),
                    TargetPrice = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Status = table.Column<string>(type: "varchar(9)", nullable: false),
                    TriggeredPrice = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    TriggeredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceAlerts", x => x.Id);
                    table.CheckConstraint("CK_PriceAlerts_Condition", "[Condition] IN ('Above', 'Below')");
                    table.CheckConstraint("CK_PriceAlerts_Status", "[Status] IN ('Active', 'Disabled', 'Triggered')");
                    table.CheckConstraint("CK_PriceAlerts_TargetPrice", "[TargetPrice] > 0");
                    table.CheckConstraint("CK_PriceAlerts_TriggeredAtUtc", "[TriggeredAtUtc] IS NULL OR [TriggeredAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_PriceAlerts_TriggeredFields", "([Status] = 'Triggered' AND [TriggeredPrice] IS NOT NULL AND [TriggeredAtUtc] IS NOT NULL) OR ([Status] IN ('Active', 'Disabled') AND [TriggeredPrice] IS NULL AND [TriggeredAtUtc] IS NULL)");
                    table.CheckConstraint("CK_PriceAlerts_TriggeredPrice", "[TriggeredPrice] IS NULL OR [TriggeredPrice] > 0");
                    table.CheckConstraint("CK_PriceAlerts_UpdatedAtUtc", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.ForeignKey(
                        name: "FK_PriceAlerts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Watchlists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Watchlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Watchlists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Holdings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                    AverageCost = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holdings", x => x.Id);
                    table.CheckConstraint("CK_Holdings_AverageCost", "[AverageCost] > 0");
                    table.CheckConstraint("CK_Holdings_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_Holdings_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Side = table.Column<string>(type: "varchar(4)", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,8)", nullable: false),
                    ExecutionPrice = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_ExecutionPrice", "[ExecutionPrice] > 0");
                    table.CheckConstraint("CK_Transactions_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_Transactions_Side", "[Side] IN ('BUY', 'SELL')");
                    table.CheckConstraint("CK_Transactions_TotalAmount", "[TotalAmount] > 0");
                    table.ForeignKey(
                        name: "FK_Transactions_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_PortfolioId_Symbol",
                table: "Holdings",
                columns: new[] { "PortfolioId", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Portfolios_UserId",
                table: "Portfolios",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceAlerts_Symbol_Currency",
                table: "PriceAlerts",
                columns: new[] { "Symbol", "Currency" },
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_PriceAlerts_UserId_Status",
                table: "PriceAlerts",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PortfolioId_ExecutedAtUtc_Id",
                table: "Transactions",
                columns: new[] { "PortfolioId", "ExecutedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PortfolioId_OrderId",
                table: "Transactions",
                columns: new[] { "PortfolioId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PortfolioId_Side_ExecutedAtUtc",
                table: "Transactions",
                columns: new[] { "PortfolioId", "Side", "ExecutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PortfolioId_Symbol_ExecutedAtUtc",
                table: "Transactions",
                columns: new[] { "PortfolioId", "Symbol", "ExecutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Watchlists_UserId_Symbol",
                table: "Watchlists",
                columns: new[] { "UserId", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holdings");

            migrationBuilder.DropTable(
                name: "PriceAlerts");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Watchlists");

            migrationBuilder.DropTable(
                name: "Portfolios");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
