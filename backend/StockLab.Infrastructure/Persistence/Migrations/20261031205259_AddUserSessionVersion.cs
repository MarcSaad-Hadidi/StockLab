using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SessionVersion",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_SessionVersion",
                table: "Users",
                sql: "[SessionVersion] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping a used counter would let old binaries accept previously revoked JWTs.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Users] WHERE [SessionVersion] > 0)
                BEGIN
                    THROW 51001, N'Cannot roll back session revocation after sessions have been revoked. Keep session-version validation enabled.', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_SessionVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "Users");
        }
    }
}
