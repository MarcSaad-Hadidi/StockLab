using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PasswordHash_NotBlank",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1024)",
                oldMaxLength: 1024);

            migrationBuilder.AddColumn<string>(
                name: "GoogleSubject",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "IX_Users_GoogleSubject",
                table: "Users",
                column: "GoogleSubject",
                unique: true,
                filter: "[GoogleSubject] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_AuthenticationMethod",
                table: "Users",
                sql: "[PasswordHash] IS NOT NULL OR [GoogleSubject] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_GoogleSubject_NotBlank",
                table: "Users",
                sql: "[GoogleSubject] IS NULL OR LTRIM(RTRIM([GoogleSubject])) <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PasswordHash_NotBlank",
                table: "Users",
                sql: "[PasswordHash] IS NULL OR LTRIM(RTRIM([PasswordHash])) <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Users] WHERE [PasswordHash] IS NULL)
                    THROW 51000, 'Cannot roll back Google authentication while Google-only accounts exist. Set a real password through recovery before rollback.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Users_GoogleSubject",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_AuthenticationMethod",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_GoogleSubject_NotBlank",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_PasswordHash_NotBlank",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GoogleSubject",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_PasswordHash_NotBlank",
                table: "Users",
                sql: "LTRIM(RTRIM([PasswordHash])) <> ''");
        }
    }
}
