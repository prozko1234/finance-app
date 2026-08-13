using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DebtOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "AlreadyHappened", not the "" EF generates for a string column: the value is an
            // enum name, and an empty one throws on the first read. It is also the only honest
            // answer for a debt written down before the app asked where the money came from —
            // that money moved without the app being told, so nothing this period pays for it
            // and no figure on any screen shifts when this migration runs.
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Debts",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "AlreadyHappened");

            migrationBuilder.AddColumn<int>(
                name: "OriginEnvelopeId",
                table: "Debts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Debts_OriginEnvelopeId",
                table: "Debts",
                column: "OriginEnvelopeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Debts_Envelopes_OriginEnvelopeId",
                table: "Debts",
                column: "OriginEnvelopeId",
                principalTable: "Envelopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Debts_Envelopes_OriginEnvelopeId",
                table: "Debts");

            migrationBuilder.DropIndex(
                name: "IX_Debts_OriginEnvelopeId",
                table: "Debts");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Debts");

            migrationBuilder.DropColumn(
                name: "OriginEnvelopeId",
                table: "Debts");
        }
    }
}
