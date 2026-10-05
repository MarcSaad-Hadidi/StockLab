using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreservePaperTradingAmountPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TotalAmount",
                table: "Transactions",
                type: "decimal(27,12)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "CashBalance",
                table: "Portfolios",
                type: "decimal(27,12)",
                nullable: false,
                defaultValue: 100000m,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)",
                oldDefaultValue: 100000m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossless rollback: only narrow when every amount fits exactly. Otherwise
            // retain the compatible wider columns rather than alter financial history.
            // Hold both tables against concurrent writes until the migration commits.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [dbo].[Portfolios] WITH (TABLOCKX, HOLDLOCK)
                    WHERE TRY_CONVERT(decimal(19,4), [CashBalance]) IS NULL
                       OR [CashBalance] <> TRY_CONVERT(decimal(19,4), [CashBalance]))
                OR EXISTS (
                    SELECT 1 FROM [dbo].[Transactions] WITH (TABLOCKX, HOLDLOCK)
                    WHERE TRY_CONVERT(decimal(19,4), [TotalAmount]) IS NULL
                       OR [TotalAmount] <> TRY_CONVERT(decimal(19,4), [TotalAmount]))
                BEGIN
                    PRINT N'PreservePaperTradingAmountPrecision rollback: retained decimal(27,12) columns to preserve financial amounts.';
                END
                ELSE
                BEGIN
                    ALTER TABLE [dbo].[Transactions] ALTER COLUMN [TotalAmount] decimal(19,4) NOT NULL;

                    DECLARE @precisionRollbackCashDefault sysname;
                    SELECT @precisionRollbackCashDefault = [name]
                    FROM sys.default_constraints
                    WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[Portfolios]')
                      AND [parent_column_id] = COLUMNPROPERTY(OBJECT_ID(N'[dbo].[Portfolios]'), N'CashBalance', 'ColumnId');
                    IF @precisionRollbackCashDefault IS NOT NULL
                    BEGIN
                        DECLARE @precisionRollbackDropDefault nvarchar(max) =
                            N'ALTER TABLE [dbo].[Portfolios] DROP CONSTRAINT ' + QUOTENAME(@precisionRollbackCashDefault);
                        EXEC sp_executesql @precisionRollbackDropDefault;
                    END

                    ALTER TABLE [dbo].[Portfolios] ALTER COLUMN [CashBalance] decimal(19,4) NOT NULL;
                    ALTER TABLE [dbo].[Portfolios] ADD DEFAULT (100000) FOR [CashBalance];
                END
                """);
        }
    }
}
