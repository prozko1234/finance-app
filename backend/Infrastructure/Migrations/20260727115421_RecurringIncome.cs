using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecurringIncome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AmountIncludesVat",
                table: "RecurringExpenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "RecurringExpenses",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Expense");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountIncludesVat",
                table: "RecurringExpenses");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "RecurringExpenses");
        }
    }
}
