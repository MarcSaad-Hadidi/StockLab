using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferredLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguage",
                table: "Users",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "en",
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PreferredLanguage",
                table: "Users",
                sql: "[PreferredLanguage] IN ('en', 'fr')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PreferredLanguage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                table: "Users");
        }
    }
}
