using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests;

/// The period is now the unit every money figure is divided over, so it has to tile the
/// calendar exactly: no day in two periods, no day in none.
public class BudgetPeriodTests
{
    [Fact]
    public void A_payday_on_the_first_is_the_calendar_month()
    {
        var p = BudgetPeriods.For(new DateOnly(2026, 7, 15), BudgetPeriods.FirstOfMonth);

        Assert.Equal(new DateOnly(2026, 7, 1), p.Start);
        Assert.Equal(new DateOnly(2026, 7, 31), p.End);
        Assert.Equal(31, p.Days);
    }

    [Fact]
    public void A_day_before_payday_still_belongs_to_the_period_that_is_running()
    {
        // Paid on the 10th; the 9th is the last day of the money that came a month ago.
        var p = BudgetPeriods.For(new DateOnly(2026, 7, 9), 10);

        Assert.Equal(new DateOnly(2026, 6, 10), p.Start);
        Assert.Equal(new DateOnly(2026, 7, 9), p.End);
    }

    [Fact]
    public void Payday_itself_starts_the_new_period()
    {
        var p = BudgetPeriods.For(new DateOnly(2026, 7, 10), 10);

        Assert.Equal(new DateOnly(2026, 7, 10), p.Start);
        Assert.Equal(new DateOnly(2026, 8, 9), p.End);
    }

    [Fact]
    public void Days_left_counts_to_the_next_payday_not_to_the_end_of_the_month()
    {
        // 25 July, paid on the 10th: 10 August is the next payday, so 9 August is the last
        // day this money has to cover — 16 days including today.
        var p = BudgetPeriods.For(new DateOnly(2026, 7, 25), 10);

        Assert.Equal(16, p.DaysLeftFrom(new DateOnly(2026, 7, 25)));
    }

    [Fact]
    public void The_last_day_still_has_one_day_left_so_the_norm_stays_divisible()
    {
        var p = BudgetPeriods.For(new DateOnly(2026, 7, 9), 10);

        Assert.Equal(1, p.DaysLeftFrom(p.End));
    }

    /// The reason the day is capped at 28 in settings — but the domain still has to answer
    /// sanely if a 29–31 ever reaches it.
    [Fact]
    public void A_day_that_february_does_not_have_falls_back_to_its_last_day()
    {
        var p = BudgetPeriods.For(new DateOnly(2026, 2, 15), 31);

        Assert.Equal(new DateOnly(2026, 1, 31), p.Start);
        Assert.Equal(new DateOnly(2026, 2, 27), p.End);
    }

    /// Clamping must not become permanent: after a short month the period has to go back to
    /// the real payday, not stay on the day February forced it to.
    [Fact]
    public void After_a_short_month_the_payday_returns_to_its_own_day()
    {
        var p = BudgetPeriods.For(new DateOnly(2026, 3, 1), 31);

        Assert.Equal(new DateOnly(2026, 2, 28), p.Start);
        Assert.Equal(new DateOnly(2026, 3, 30), p.End);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(28)]
    [InlineData(31)]
    public void Periods_tile_a_whole_year_with_no_gap_and_no_overlap(int startDay)
    {
        var day = new DateOnly(2026, 1, 1);
        var previous = BudgetPeriods.For(day, startDay);

        // Two years, day by day: every date is inside its own period, and a period only
        // ever changes to one starting the day after the last one ended.
        for (var i = 0; i < 730; i++, day = day.AddDays(1))
        {
            var p = BudgetPeriods.For(day, startDay);

            Assert.True(p.Contains(day));
            Assert.True(p == previous || p.Start == previous.End.AddDays(1));

            previous = p;
        }
    }
}
