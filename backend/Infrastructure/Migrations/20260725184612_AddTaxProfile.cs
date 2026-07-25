using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Regime = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RyczaltRate = table.Column<decimal>(type: "TEXT", precision: 6, scale: 4, nullable: false),
                    VatPayer = table.Column<bool>(type: "INTEGER", nullable: false),
                    VatRate = table.Column<decimal>(type: "TEXT", precision: 6, scale: 4, nullable: false),
                    ZusType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ZusSocial = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    HealthContribution = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Chorobowe = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxProfiles", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxProfiles");
        }
    }
}
