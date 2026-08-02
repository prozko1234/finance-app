using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests;

/// The rule that decides when money leaves. Everything else about recurring charges —
/// materializing them, reserving them, showing the next one — reads its answers from here.
public class RecurringScheduleTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    private static List<DateOnly> Occurrences(
        string anchor, RecurrenceUnit unit, int interval, string from, string to) =>
        RecurringSchedule.Occurrences(D(anchor), unit, interval, D(from), D(to)).ToList();

    [Fact]
    public void Weekly_lands_on_the_same_weekday_every_week()
    {
        var dates = Occurrences("2026-08-03", RecurrenceUnit.Week, 1, "2026-08-01", "2026-08-31");

        Assert.Equal(
            [D("2026-08-03"), D("2026-08-10"), D("2026-08-17"), D("2026-08-24"), D("2026-08-31")],
            dates);
        Assert.All(dates, d => Assert.Equal(DayOfWeek.Monday, d.DayOfWeek));
    }

    [Fact]
    public void Every_second_week_skips_one()
    {
        var dates = Occurrences("2026-08-03", RecurrenceUnit.Week, 2, "2026-08-01", "2026-09-15");

        Assert.Equal(
            [D("2026-08-03"), D("2026-08-17"), D("2026-08-31"), D("2026-09-14")],
            dates);
    }

    [Fact]
    public void Monthly_keeps_the_day_of_the_month()
    {
        var dates = Occurrences("2026-01-10", RecurrenceUnit.Month, 1, "2026-01-01", "2026-04-30");

        Assert.Equal([D("2026-01-10"), D("2026-02-10"), D("2026-03-10"), D("2026-04-10")], dates);
    }

    [Fact]
    public void A_quarter_is_three_months_not_a_unit_of_its_own()
    {
        var dates = Occurrences("2026-01-15", RecurrenceUnit.Month, 3, "2026-01-01", "2026-12-31");

        Assert.Equal([D("2026-01-15"), D("2026-04-15"), D("2026-07-15"), D("2026-10-15")], dates);
    }

    [Fact]
    public void Yearly_comes_back_once_a_year()
    {
        var dates = Occurrences("2024-06-01", RecurrenceUnit.Year, 1, "2026-01-01", "2027-12-31");

        Assert.Equal([D("2026-06-01"), D("2027-06-01")], dates);
    }

    /// The reason occurrences are counted from the anchor instead of from the previous date:
    /// February would otherwise pull the whole series down to the 28th and keep it there.
    [Fact]
    public void A_charge_on_the_31st_bends_for_February_and_springs_back()
    {
        var dates = Occurrences("2026-01-31", RecurrenceUnit.Month, 1, "2026-01-01", "2026-05-31");

        Assert.Equal(
            [D("2026-01-31"), D("2026-02-28"), D("2026-03-31"), D("2026-04-30"), D("2026-05-31")],
            dates);
    }

    [Fact]
    public void The_29th_of_February_survives_to_the_next_leap_year()
    {
        var dates = Occurrences("2024-02-29", RecurrenceUnit.Year, 1, "2025-01-01", "2028-12-31");

        Assert.Equal(
            [D("2025-02-28"), D("2026-02-28"), D("2027-02-28"), D("2028-02-29")],
            dates);
    }

    [Fact]
    public void Nothing_is_owed_before_the_first_charge()
    {
        var dates = Occurrences("2026-09-01", RecurrenceUnit.Month, 1, "2026-01-01", "2026-08-31");

        Assert.Empty(dates);
    }

    [Fact]
    public void A_window_that_starts_after_it_ends_yields_nothing()
    {
        Assert.Empty(Occurrences("2026-01-01", RecurrenceUnit.Month, 1, "2026-05-01", "2026-04-01"));
    }

    [Fact]
    public void A_broken_interval_yields_nothing_rather_than_looping()
    {
        Assert.Empty(Occurrences("2026-01-01", RecurrenceUnit.Month, 0, "2026-01-01", "2026-12-31"));
    }

    [Fact]
    public void A_window_long_after_the_anchor_still_starts_in_the_right_place()
    {
        // The arithmetic shortcut that skips ahead instead of walking week by week: get it
        // wrong and this returns the wrong Mondays, or none at all.
        var dates = Occurrences("2020-01-06", RecurrenceUnit.Week, 1, "2026-08-01", "2026-08-31");

        Assert.Equal(
            [D("2026-08-03"), D("2026-08-10"), D("2026-08-17"), D("2026-08-24"), D("2026-08-31")],
            dates);
    }

    [Fact]
    public void Next_after_today_skips_a_charge_due_today()
    {
        // Today's charge has already been written by the time anything reads this, so the
        // question "when next?" must not answer "today".
        var next = RecurringSchedule.NextAfter(D("2026-08-10"), RecurrenceUnit.Month, 1, D("2026-08-10"));

        Assert.Equal(D("2026-09-10"), next);
    }

    [Fact]
    public void Next_after_reaches_across_a_whole_year()
    {
        var next = RecurringSchedule.NextAfter(D("2026-06-01"), RecurrenceUnit.Year, 1, D("2026-06-01"));

        Assert.Equal(D("2027-06-01"), next);
    }

    [Fact]
    public void Next_after_finds_the_first_charge_of_a_schedule_that_has_not_started()
    {
        var next = RecurringSchedule.NextAfter(D("2026-09-15"), RecurrenceUnit.Month, 1, D("2026-08-02"));

        Assert.Equal(D("2026-09-15"), next);
    }
}
