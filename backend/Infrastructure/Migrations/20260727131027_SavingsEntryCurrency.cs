using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SavingsEntryCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "SavingsEntries",
                newName: "AmountOriginal");

            migrationBuilder.AddColumn<decimal>(
                name: "AmountBase",
                table: "SavingsEntries",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyOriginal",
                table: "SavingsEntries",
                type: "TEXT",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "FxDate",
                table: "SavingsEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<decimal>(
                name: "FxRate",
                table: "SavingsEntries",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            // Every existing movement was made in base currency, and the rename above moved
            // its value into AmountOriginal. Without this backfill AmountBase stays 0 and the
            // balance — which is the sum of these rows — silently collapses to zero.
            migrationBuilder.Sql(
                """
                UPDATE SavingsEntries
                SET AmountBase = AmountOriginal,
                    CurrencyOriginal = 'PLN',
                    FxRate = '1',
                    FxDate = Date
                WHERE CurrencyOriginal = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountBase",
                table: "SavingsEntries");

            migrationBuilder.DropColumn(
                name: "CurrencyOriginal",
                table: "SavingsEntries");

            migrationBuilder.DropColumn(
                name: "FxDate",
                table: "SavingsEntries");

            migrationBuilder.DropColumn(
                name: "FxRate",
                table: "SavingsEntries");

            migrationBuilder.RenameColumn(
                name: "AmountOriginal",
                table: "SavingsEntries",
                newName: "Amount");
        }
    }
}
