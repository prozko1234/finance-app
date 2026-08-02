using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// Recurring charges stop being monthly-only: an anchor date plus "every N weeks/months/
    /// years" replaces DayOfMonth.
    ///
    /// Written by hand. The scaffolder read this as a rename of DayOfMonth to Interval, which
    /// would have turned "the 10th of the month" into "every 10 months" and left Unit empty.
    /// <inheritdoc />
    public partial class RecurringCadence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "StartsOn",
                table: "RecurringExpenses",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "RecurringExpenses",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "Month");

            migrationBuilder.AddColumn<int>(
                name: "Interval",
                table: "RecurringExpenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Every existing row is monthly, so the only thing to work out is the anchor: the
            // charge day it already had, in the month it was created in. If that day had
            // already passed by the time the row was created, its first charge was the month
            // after — anchoring earlier would invent a charge for a date on which the
            // subscription did not yet exist.
            //
            // The day is clamped to the length of the target month, because 31 is a legal
            // DayOfMonth and February is not 31 days long.
            const string dayInMonthOf = """
                date(
                  date(CreatedAt, 'start of month', {0}),
                  '+' || (
                    min(
                      max(DayOfMonth, 1),
                      cast(strftime('%d', date(CreatedAt, 'start of month', {1}, '-1 day')) AS INTEGER)
                    ) - 1
                  ) || ' days'
                )
                """;

            var thisMonth = string.Format(dayInMonthOf, "'+0 months'", "'+1 month'");
            var nextMonth = string.Format(dayInMonthOf, "'+1 month'", "'+2 months'");
            var firstCharge = $"CASE WHEN {thisMonth} >= date(CreatedAt) THEN {thisMonth} ELSE {nextMonth} END";

            // A clamped anchor would be a trap: rent on the 31st anchored in February becomes
            // the 28th, and every later charge follows it there. So when the day had to be
            // clamped, the anchor moves to the month after — which always has 31 days, because
            // every short month is followed by a long one (Feb→Mar, Apr→May, Jun→Jul, Sep→Oct,
            // Nov→Dec). The cost is skipping at most one already-clamped charge; if it has
            // already happened its transaction exists either way, and inventing money is worse
            // than missing one row.
            migrationBuilder.Sql($"""
                UPDATE RecurringExpenses
                SET Unit = 'Month',
                    Interval = 1,
                    StartsOn = CASE
                      WHEN cast(strftime('%d', {firstCharge}) AS INTEGER) = max(DayOfMonth, 1)
                        THEN {firstCharge}
                      ELSE date(
                             {firstCharge},
                             'start of month',
                             '+1 month',
                             '+' || (max(DayOfMonth, 1) - 1) || ' days')
                    END
                WHERE CreatedAt IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "DayOfMonth",
                table: "RecurringExpenses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DayOfMonth",
                table: "RecurringExpenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // Only monthly rows survive the trip back — a weekly schedule has no day of month
            // to go home to, which is the whole reason this migration exists.
            migrationBuilder.Sql(
                "UPDATE RecurringExpenses SET DayOfMonth = cast(strftime('%d', StartsOn) AS INTEGER);");

            migrationBuilder.DropColumn(name: "StartsOn", table: "RecurringExpenses");
            migrationBuilder.DropColumn(name: "Unit", table: "RecurringExpenses");
            migrationBuilder.DropColumn(name: "Interval", table: "RecurringExpenses");
        }
    }
}
