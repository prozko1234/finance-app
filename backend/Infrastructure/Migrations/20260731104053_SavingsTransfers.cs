using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SavingsTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TransferKey",
                table: "SavingsEntries",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavingsEntries_TransferKey",
                table: "SavingsEntries",
                column: "TransferKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SavingsEntries_TransferKey",
                table: "SavingsEntries");

            migrationBuilder.DropColumn(
                name: "TransferKey",
                table: "SavingsEntries");
        }
    }
}
