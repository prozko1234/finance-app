using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Envelopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order matters. The envelope table and its default row have to exist BEFORE the
            // column referencing them is added and backfilled — otherwise every existing
            // savings movement is left pointing at envelope 0, which is no envelope at all,
            // and the foreign key below rebuilds the table around orphaned rows.
            migrationBuilder.CreateTable(
                name: "Envelopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Envelopes", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "Envelopes" ("Name", "Kind", "IsDefault", "CreatedAt")
                VALUES ('Заощадження', 'Savings', 1, '0001-01-01 00:00:00+00:00');
                """);

            migrationBuilder.AddColumn<int>(
                name: "EnvelopeId",
                table: "SavingsEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Everything saved before envelopes existed WAS the savings pot, so it belongs to
            // the default envelope. Skipping this would zero the balance the user can see.
            migrationBuilder.Sql(
                """
                UPDATE "SavingsEntries"
                SET "EnvelopeId" = (SELECT "Id" FROM "Envelopes" WHERE "IsDefault" = 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SavingsEntries_EnvelopeId",
                table: "SavingsEntries",
                column: "EnvelopeId");

            migrationBuilder.CreateIndex(
                name: "IX_Envelopes_Name",
                table: "Envelopes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SavingsEntries_Envelopes_EnvelopeId",
                table: "SavingsEntries",
                column: "EnvelopeId",
                principalTable: "Envelopes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SavingsEntries_Envelopes_EnvelopeId",
                table: "SavingsEntries");

            migrationBuilder.DropTable(
                name: "Envelopes");

            migrationBuilder.DropIndex(
                name: "IX_SavingsEntries_EnvelopeId",
                table: "SavingsEntries");

            migrationBuilder.DropColumn(
                name: "EnvelopeId",
                table: "SavingsEntries");
        }
    }
}
