using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllocationSchemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllocationSchemes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Preset = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationSchemes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AllocationBuckets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SchemeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Percent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationBuckets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationBuckets_AllocationSchemes_SchemeId",
                        column: x => x.SchemeId,
                        principalTable: "AllocationSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AllocationSchemes",
                columns: new[] { "Id", "IsActive", "Name", "Preset", "UpdatedAt" },
                values: new object[] { 1, true, "Тільки денна норма", "daily-norm-only", new DateTimeOffset(new DateTime(2026, 7, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.InsertData(
                table: "AllocationBuckets",
                columns: new[] { "Id", "Kind", "Name", "Percent", "SchemeId", "SortOrder" },
                values: new object[] { 1, "Spending", "На витрати", 100m, 1, 0 });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationBuckets_SchemeId",
                table: "AllocationBuckets",
                column: "SchemeId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationSchemes_IsActive",
                table: "AllocationSchemes",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllocationBuckets");

            migrationBuilder.DropTable(
                name: "AllocationSchemes");
        }
    }
}
