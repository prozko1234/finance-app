using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests;

public class SafeToSpendTests
{
    private static readonly DateOnly MidJuly = new(2026, 7, 15); // 17 days left, incl. today

    /// A calendar month, which is what a payday on the 1st produces — the arithmetic these
    /// tests pin down is about days left in a period, not about which day it starts on.
    private static readonly BudgetPeriod JulyMonth =
        BudgetPeriods.For(MidJuly, BudgetPeriods.FirstOfMonth);

    [Fact]
    public void No_budget_returns_not_set_and_null_figures()
    {
        var r = SafeToSpendCalculator.Calculate(null, spentThisPeriod: 120m, spentToday: 20m,
            reservedRecurring: 0m, today: MidJuly, period: JulyMonth);

        Assert.False(r.BudgetSet);
        Assert.Null(r.DailyNorm);
        Assert.Null(r.LeftToday);
        Assert.Null(r.RemainingThisPeriod);
        Assert.Equal(120m, r.SpentThisPeriod);
        Assert.Equal(17, r.DaysLeftInPeriod); // 31 - 15 + 1
    }

    [Fact]
    public void Mid_month_divides_remaining_by_days_left_floored()
    {
        // 3000 / 17 = 176.4705... -> 176.47
        var r = SafeToSpendCalculator.Calculate(3000m, 0m, 0m, 0m, MidJuly, JulyMonth);

        Assert.True(r.BudgetSet);
        Assert.Equal(3000m, r.RemainingThisPeriod);
        Assert.Equal(176.47m, r.DailyNorm);
        Assert.Equal(176.47m, r.LeftToday); // nothing spent today yet
    }

    [Fact]
    public void Reserved_recurring_reduces_remaining()
    {
        // (3000 - 500) / 17 = 147.05...
        var r = SafeToSpendCalculator.Calculate(3000m, 0m, 0m, 500m, MidJuly, JulyMonth);

        Assert.Equal(2500m, r.RemainingThisPeriod);
        Assert.Equal(147.05m, r.DailyNorm);
    }

    [Fact]
    public void Number_is_stable_whether_amount_is_reserved_or_spent()
    {
        // When a reserved recurring charges, it moves reserved -> spent (on an earlier day).
        var beforeCharge = SafeToSpendCalculator.Calculate(3000m, 200m, 0m, 500m, MidJuly, JulyMonth);
        var afterCharge = SafeToSpendCalculator.Calculate(3000m, 700m, 0m, 0m, MidJuly, JulyMonth);

        Assert.Equal(beforeCharge.RemainingThisPeriod, afterCharge.RemainingThisPeriod);
        Assert.Equal(beforeCharge.DailyNorm, afterCharge.DailyNorm);
    }

    /// The heart of M15: the norm must NOT move when money is spent today, otherwise
    /// "over the norm" is unsayable — the target would always slide to match the spending.
    [Fact]
    public void Todays_norm_is_fixed_at_the_start_of_the_day()
    {
        var morning = SafeToSpendCalculator.Calculate(3000m, 0m, spentToday: 0m, 0m, MidJuly, JulyMonth);
        var evening = SafeToSpendCalculator.Calculate(3000m, 500m, spentToday: 500m, 0m, MidJuly, JulyMonth);

        Assert.Equal(morning.DailyNorm, evening.DailyNorm);
        Assert.Equal(500m, evening.SpentToday);
    }

    [Fact]
    public void Overspending_today_shows_a_negative_left_today()
    {
        // Norm 176.47, spent 300 -> 123.53 over.
        var r = SafeToSpendCalculator.Calculate(3000m, 300m, 300m, 0m, MidJuly, JulyMonth);

        Assert.Equal(176.47m, r.DailyNorm);
        Assert.Equal(-123.53m, r.LeftToday);
    }

    [Fact]
    public void Overspending_today_lowers_tomorrow_and_the_gap_is_visible()
    {
        var r = SafeToSpendCalculator.Calculate(3000m, 300m, 300m, 0m, MidJuly, JulyMonth);

        // Had today stayed on plan: (3000 - 176.47) / 16 = 176.47
        // Having spent 300 instead: (3000 - 300)    / 16 = 168.75
        Assert.Equal(176.47m, r.TomorrowIfOnPlan);
        Assert.Equal(168.75m, r.TomorrowIfStop);
        Assert.True(r.TomorrowIfStop < r.TomorrowIfOnPlan); // the consequence, stated plainly
    }

    [Fact]
    public void Underspending_today_raises_tomorrow()
    {
        var r = SafeToSpendCalculator.Calculate(3000m, 50m, 50m, 0m, MidJuly, JulyMonth);

        Assert.Equal(126.47m, r.LeftToday); // 176.47 - 50
        Assert.True(r.TomorrowIfStop > r.TomorrowIfOnPlan);
    }

    [Fact]
    public void Last_day_of_month_has_no_tomorrow_to_project()
    {
        var r = SafeToSpendCalculator.Calculate(1000m, 700m, 0m, 0m, new DateOnly(2026, 7, 31), Month(new DateOnly(2026, 7, 31)));

        Assert.Equal(1, r.DaysLeftInPeriod);
        Assert.Equal(300m, r.DailyNorm); // the whole rest of the budget is today's
        Assert.Null(r.TomorrowIfStop);
        Assert.Null(r.TomorrowIfOnPlan);
    }

    [Fact]
    public void Subtracts_spending_from_budget()
    {
        // Spent on earlier days: (2000 - 500) / 31 = 48.387 -> 48.38
        var r = SafeToSpendCalculator.Calculate(2000m, 500m, 0m, 0m, new DateOnly(2026, 7, 1), Month(new DateOnly(2026, 7, 1)));

        Assert.Equal(1500m, r.RemainingThisPeriod);
        Assert.Equal(31, r.DaysLeftInPeriod);
        Assert.Equal(48.38m, r.DailyNorm);
    }

    [Fact]
    public void Overspent_shows_negative_honestly()
    {
        var r = SafeToSpendCalculator.Calculate(1000m, 1200m, 0m, 0m, new DateOnly(2026, 7, 31), Month(new DateOnly(2026, 7, 31)));

        Assert.Equal(-200m, r.RemainingThisPeriod);
        Assert.Equal(-200m, r.DailyNorm);
    }

    [Fact]
    public void February_leap_year_day_count()
    {
        // 2028 is a leap year, February has 29 days. today 2028-02-20 -> 29 - 20 + 1 = 10
        var r = SafeToSpendCalculator.Calculate(1000m, 0m, 0m, 0m, new DateOnly(2028, 2, 20), Month(new DateOnly(2028, 2, 20)));

        Assert.Equal(10, r.DaysLeftInPeriod);
        Assert.Equal(100m, r.DailyNorm);
    }
    /// The calendar month around a date — a payday on the 1st.
    private static BudgetPeriod Month(DateOnly date) =>
        BudgetPeriods.For(date, BudgetPeriods.FirstOfMonth);
}
