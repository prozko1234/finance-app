using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecurringExpenseId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringExpenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AmountOriginal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CurrencyOriginal = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RecurringExpenseId_Date",
                table: "Transactions",
                columns: new[] { "RecurringExpenseId", "Date" },
                unique: true,
                filter: "\"RecurringExpenseId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_CategoryId",
                table: "RecurringExpenses",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_RecurringExpenses_RecurringExpenseId",
                table: "Transactions",
                column: "RecurringExpenseId",
                principalTable: "RecurringExpenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_RecurringExpenses_RecurringExpenseId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "RecurringExpenses");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_RecurringExpenseId_Date",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RecurringExpenseId",
                table: "Transactions");
        }
    }
}
