using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpendFromEnvelope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Transactions");

            migrationBuilder.AddColumn<int>(
                name: "EnvelopeId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_EnvelopeId",
                table: "Transactions",
                column: "EnvelopeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Envelopes_EnvelopeId",
                table: "Transactions",
                column: "EnvelopeId",
                principalTable: "Envelopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Envelopes_EnvelopeId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_EnvelopeId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "EnvelopeId",
                table: "Transactions");

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "Transactions",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "");
        }
    }
}
