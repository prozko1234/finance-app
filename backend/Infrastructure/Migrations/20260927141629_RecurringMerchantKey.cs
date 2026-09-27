using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecurringMerchantKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MerchantKey",
                table: "RecurringExpenses",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_MerchantKey",
                table: "RecurringExpenses",
                column: "MerchantKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecurringExpenses_MerchantKey",
                table: "RecurringExpenses");

            migrationBuilder.DropColumn(
                name: "MerchantKey",
                table: "RecurringExpenses");
        }
    }
}
