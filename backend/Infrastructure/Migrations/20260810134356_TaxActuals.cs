using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaxActuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxActuals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ZusSocial = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Health = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Pit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxActuals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxActuals_UserId",
                table: "TaxActuals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxActuals_UserId_Month",
                table: "TaxActuals",
                columns: new[] { "UserId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxActuals");
        }
    }
}
