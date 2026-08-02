using FinanceApp.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Api.Tests;

/// The one-shot rewrite of real money data: every existing subscription had a DayOfMonth and
/// has to come out the other side charging on the same day. It runs once, on a database that
/// cannot be re-made, so it is worth a test that actually migrates rather than a reading of
/// the SQL.
public class RecurringCadenceMigrationTests : IDisposable
{
    /// The migration immediately before the one under test.
    private const string Before = "DeviceTokens";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public RecurringCadenceMigrationTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// Inserts a row the way the old schema stored it, then runs the migration and reports
    /// the anchor it produced.
    private async Task<DateOnly> MigrateRowAsync(int dayOfMonth, string createdAt)
    {
        await using (var old = NewContext())
        {
            await old.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);

            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO Categories (Name, SortOrder, IsSystem) VALUES ('Тест', 0, 0);");
            await old.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO RecurringExpenses
                  (Kind, AmountIncludesVat, AmountOriginal, CurrencyOriginal, CategoryId,
                   DayOfMonth, Active, CreatedAt)
                VALUES ('Expense', 1, '49.99', 'PLN', 1, {0}, 1, {1});
                """,
                dayOfMonth, createdAt);
        }

        await using var migrated = NewContext();
        await migrated.Database.MigrateAsync();

        return (await migrated.RecurringExpenses.SingleAsync()).StartsOn;
    }

    [Fact]
    public async Task A_charge_day_still_ahead_anchors_in_the_month_it_was_created()
    {
        var anchor = await MigrateRowAsync(dayOfMonth: 20, createdAt: "2026-07-15 09:00:00+00:00");

        Assert.Equal(new DateOnly(2026, 7, 20), anchor);
    }

    /// The charge day had already gone by when the subscription was set up, so its first
    /// charge was the month after. Anchoring on the earlier date would have the materializer
    /// write a charge for a day the subscription did not exist on.
    [Fact]
    public async Task A_charge_day_already_past_anchors_in_the_following_month()
    {
        var anchor = await MigrateRowAsync(dayOfMonth: 10, createdAt: "2026-07-25 09:00:00+00:00");

        Assert.Equal(new DateOnly(2026, 8, 10), anchor);
    }

    [Fact]
    public async Task Anchoring_on_the_31st_does_not_get_stuck_on_the_28th()
    {
        // February cannot hold the 31st. Anchoring there would move every later charge to the
        // 28th for good — so the anchor moves to March, which can.
        var anchor = await MigrateRowAsync(dayOfMonth: 31, createdAt: "2026-02-05 09:00:00+00:00");

        Assert.Equal(new DateOnly(2026, 3, 31), anchor);
    }

    [Fact]
    public async Task The_30th_survives_February_too()
    {
        var anchor = await MigrateRowAsync(dayOfMonth: 30, createdAt: "2026-02-05 09:00:00+00:00");

        Assert.Equal(new DateOnly(2026, 3, 30), anchor);
    }

    [Fact]
    public async Task The_31st_in_a_long_month_stays_where_it_is()
    {
        var anchor = await MigrateRowAsync(dayOfMonth: 31, createdAt: "2026-01-05 09:00:00+00:00");

        Assert.Equal(new DateOnly(2026, 1, 31), anchor);
    }

    [Fact]
    public async Task Every_migrated_row_comes_out_monthly()
    {
        await MigrateRowAsync(dayOfMonth: 10, createdAt: "2026-07-01 09:00:00+00:00");

        await using var db = NewContext();
        var row = await db.RecurringExpenses.SingleAsync();

        Assert.Equal(Domain.RecurrenceUnit.Month, row.Unit);
        Assert.Equal(1, row.Interval);
    }
}
