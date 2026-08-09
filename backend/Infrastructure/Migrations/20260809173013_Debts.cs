using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Debts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Debts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Person = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    AmountOriginal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CurrencyOriginal = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountBase = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FxRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    FxDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Deadline = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReserveFromBudget = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClosedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Debts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DebtPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DebtId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AmountOriginal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CurrencyOriginal = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountBase = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    FxRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    FxDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EnvelopeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebtPayments_Debts_DebtId",
                        column: x => x.DebtId,
                        principalTable: "Debts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DebtPayments_Envelopes_EnvelopeId",
                        column: x => x.EnvelopeId,
                        principalTable: "Envelopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DebtPayments_Date",
                table: "DebtPayments",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_DebtPayments_DebtId",
                table: "DebtPayments",
                column: "DebtId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtPayments_EnvelopeId",
                table: "DebtPayments",
                column: "EnvelopeId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtPayments_UserId",
                table: "DebtPayments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Debts_Date",
                table: "Debts",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Debts_UserId",
                table: "Debts",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DebtPayments");

            migrationBuilder.DropTable(
                name: "Debts");
        }
    }
}
