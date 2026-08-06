using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultiUserOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PeriodCarryovers_PeriodStart",
                table: "PeriodCarryovers");

            migrationBuilder.DropIndex(
                name: "IX_MerchantRules_Key",
                table: "MerchantRules");

            migrationBuilder.DropIndex(
                name: "IX_Envelopes_Name",
                table: "Envelopes");

            migrationBuilder.DropIndex(
                name: "IX_AllocationSchemes_IsActive",
                table: "AllocationSchemes");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "TaxProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "SavingsPlans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "SavingsEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "RecurringSkips",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "RecurringExpenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "PeriodCarryovers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "OpeningBalances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "MerchantRules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Envelopes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "AllocationSchemes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "AllocationBuckets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Everything that existed before accounts did belongs to the one account that
            // existed: the owner. Written as raw SQL because a data migration is the whole
            // point of this step — the columns above landed with a default of 0, which is
            // nobody, and a row owned by nobody is a row no query will ever return again.
            //
            // MIN(Id) rather than a literal 1: the owner is whoever registered first, and on
            // a database that has been through a restore that is not guaranteed to be id 1.
            foreach (var table in new[]
                     {
                         "Transactions", "Categories", "OpeningBalances", "PeriodCarryovers",
                         "RecurringExpenses", "RecurringSkips", "TaxProfiles", "SavingsPlans",
                         "SavingsEntries", "Envelopes", "AllocationSchemes", "AllocationBuckets",
                         "AppSettings", "MerchantRules",
                     })
            {
                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"UserId\" = (SELECT MIN(\"Id\") FROM \"Users\") " +
                    "WHERE EXISTS (SELECT 1 FROM \"Users\");");
            }

            // The starting categories and the default scheme used to be model seed data, so
            // the early migrations INSERT them into every database that has ever run them.
            // They are provisioned per account now, which leaves the old rows in two very
            // different situations:
            //
            //   - a database with an owner (this one, in production) — those rows ARE the
            //     owner's categories, with a year of transactions pointing at them. The
            //     backfill above just handed them over, and they must not be touched again.
            //   - a database with no owner yet (a fresh deploy, every integration test) —
            //     they belong to nobody, no query will ever return them, and the first
            //     account to register would be given a second set beside them.
            //
            // So they are cleared only in the second case. Nothing can reference them there:
            // an account is what creates data, and there is not one.
            // Matched by the ids the seed actually used — Categories 1-10, one scheme, one
            // bucket — rather than by "everything unowned". Those ids are a historical fact,
            // and being exact means a row that merely happens to have no owner (anything a
            // test or a script wrote straight past the app) is left alone.
            foreach (var (table, ids) in new[]
                     {
                         ("AllocationBuckets", "1"),
                         ("AllocationSchemes", "1"),
                         ("Categories", "1,2,3,4,5,6,7,8,9,10"),
                     })
            {
                migrationBuilder.Sql(
                    $"DELETE FROM \"{table}\" " +
                    $"WHERE \"Id\" IN ({ids}) AND \"UserId\" = 0 " +
                    "AND NOT EXISTS (SELECT 1 FROM \"Users\");");

                // Those tables are AUTOINCREMENT, so SQLite would carry on from the highest id
                // it ever handed out and the first real account would start at 11. Nothing
                // breaks either way, but a brand-new database numbering its categories from 1
                // is the behaviour everything written before this migration expects, and there
                // is no reason to make a fresh install look different from the one it replaces.
                migrationBuilder.Sql(
                    $"DELETE FROM sqlite_sequence WHERE name = '{table}' " +
                    "AND NOT EXISTS (SELECT 1 FROM \"Users\");");
            }

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxProfiles_UserId",
                table: "TaxProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsPlans_UserId",
                table: "SavingsPlans",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsEntries_UserId",
                table: "SavingsEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringSkips_UserId",
                table: "RecurringSkips",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_UserId",
                table: "RecurringExpenses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryovers_UserId",
                table: "PeriodCarryovers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryovers_UserId_PeriodStart",
                table: "PeriodCarryovers",
                columns: new[] { "UserId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalances_UserId",
                table: "OpeningBalances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_UserId",
                table: "MerchantRules",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_UserId_Key",
                table: "MerchantRules",
                columns: new[] { "UserId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Envelopes_UserId",
                table: "Envelopes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Envelopes_UserId_Name",
                table: "Envelopes",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId",
                table: "Categories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_UserId",
                table: "AppSettings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationSchemes_UserId",
                table: "AllocationSchemes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationSchemes_UserId_IsActive",
                table: "AllocationSchemes",
                columns: new[] { "UserId", "IsActive" },
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationBuckets_UserId",
                table: "AllocationBuckets",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_TaxProfiles_UserId",
                table: "TaxProfiles");

            migrationBuilder.DropIndex(
                name: "IX_SavingsPlans_UserId",
                table: "SavingsPlans");

            migrationBuilder.DropIndex(
                name: "IX_SavingsEntries_UserId",
                table: "SavingsEntries");

            migrationBuilder.DropIndex(
                name: "IX_RecurringSkips_UserId",
                table: "RecurringSkips");

            migrationBuilder.DropIndex(
                name: "IX_RecurringExpenses_UserId",
                table: "RecurringExpenses");

            migrationBuilder.DropIndex(
                name: "IX_PeriodCarryovers_UserId",
                table: "PeriodCarryovers");

            migrationBuilder.DropIndex(
                name: "IX_PeriodCarryovers_UserId_PeriodStart",
                table: "PeriodCarryovers");

            migrationBuilder.DropIndex(
                name: "IX_OpeningBalances_UserId",
                table: "OpeningBalances");

            migrationBuilder.DropIndex(
                name: "IX_MerchantRules_UserId",
                table: "MerchantRules");

            migrationBuilder.DropIndex(
                name: "IX_MerchantRules_UserId_Key",
                table: "MerchantRules");

            migrationBuilder.DropIndex(
                name: "IX_Envelopes_UserId",
                table: "Envelopes");

            migrationBuilder.DropIndex(
                name: "IX_Envelopes_UserId_Name",
                table: "Envelopes");

            migrationBuilder.DropIndex(
                name: "IX_Categories_UserId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_AppSettings_UserId",
                table: "AppSettings");

            migrationBuilder.DropIndex(
                name: "IX_AllocationSchemes_UserId",
                table: "AllocationSchemes");

            migrationBuilder.DropIndex(
                name: "IX_AllocationSchemes_UserId_IsActive",
                table: "AllocationSchemes");

            migrationBuilder.DropIndex(
                name: "IX_AllocationBuckets_UserId",
                table: "AllocationBuckets");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "TaxProfiles");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "SavingsPlans");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "SavingsEntries");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "RecurringSkips");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "RecurringExpenses");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "PeriodCarryovers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "OpeningBalances");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "MerchantRules");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Envelopes");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AllocationSchemes");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AllocationBuckets");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodCarryovers_PeriodStart",
                table: "PeriodCarryovers",
                column: "PeriodStart",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_Key",
                table: "MerchantRules",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Envelopes_Name",
                table: "Envelopes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AllocationSchemes_IsActive",
                table: "AllocationSchemes",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = 1");
        }
    }
}
