using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetExpiresAtUtc",
                table: "Users",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetRequestedAtUtc",
                table: "Users",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PasswordResetTokenHash",
                table: "Users",
                type: "varbinary(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordResetExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetRequestedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "Users");
        }
    }
}
