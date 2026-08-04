using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PeriodCarryover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeriodCarryovers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AmountBase = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EnvelopeId = table.Column<int>(type: "INTEGER", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodCarryovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodCarryovers_Envelopes_EnvelopeId",
                        column: x => x.EnvelopeId,
                        principalTable: "Envelopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryovers_EnvelopeId",
                table: "PeriodCarryovers",
                column: "EnvelopeId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryovers_PeriodStart",
                table: "PeriodCarryovers",
                column: "PeriodStart",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeriodCarryovers");
        }
    }
}
