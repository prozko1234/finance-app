using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests;

public class SafeToSpendTests
{
    [Fact]
    public void No_budget_returns_not_set_and_null_figure()
    {
        var r = SafeToSpendCalculator.Calculate(null, spentThisMonth: 120m, reservedRecurring: 0m, today: new DateOnly(2026, 7, 15));

        Assert.False(r.BudgetSet);
        Assert.Null(r.SafeToSpendToday);
        Assert.Null(r.RemainingThisMonth);
        Assert.Equal(120m, r.SpentThisMonth);
        Assert.Equal(17, r.DaysLeftInMonth); // 31 - 15 + 1
    }

    [Fact]
    public void Mid_month_divides_remaining_by_days_left_floored()
    {
        // 3000 - 0 - 0 = 3000, July: 31 - 15 + 1 = 17 days. 3000/17 = 176.4705... -> 176.47
        var r = SafeToSpendCalculator.Calculate(3000m, 0m, 0m, new DateOnly(2026, 7, 15));

        Assert.True(r.BudgetSet);
        Assert.Equal(3000m, r.RemainingThisMonth);
        Assert.Equal(17, r.DaysLeftInMonth);
        Assert.Equal(176.47m, r.SafeToSpendToday);
    }

    [Fact]
    public void Reserved_recurring_reduces_remaining()
    {
        // (3000 - 0 - 500) / 17 = 2500/17 = 147.05... -> 147.05
        var r = SafeToSpendCalculator.Calculate(3000m, 0m, 500m, new DateOnly(2026, 7, 15));

        Assert.Equal(500m, r.ReservedRecurring);
        Assert.Equal(2500m, r.RemainingThisMonth);
        Assert.Equal(147.05m, r.SafeToSpendToday);
    }

    [Fact]
    public void Number_is_stable_whether_amount_is_reserved_or_spent()
    {
        // Key property: when a reserved recurring charges, it moves reserved -> spent.
        // The remaining (and thus the figure) must not change.
        var beforeCharge = SafeToSpendCalculator.Calculate(3000m, 200m, 500m, new DateOnly(2026, 7, 15));
        var afterCharge = SafeToSpendCalculator.Calculate(3000m, 700m, 0m, new DateOnly(2026, 7, 15));

        Assert.Equal(beforeCharge.RemainingThisMonth, afterCharge.RemainingThisMonth);
        Assert.Equal(beforeCharge.SafeToSpendToday, afterCharge.SafeToSpendToday);
    }

    [Fact]
    public void Subtracts_spending_from_budget()
    {
        // (2000 - 500 - 0) / (31 - 1 + 1 = 31) = 1500/31 = 48.387 -> 48.38
        var r = SafeToSpendCalculator.Calculate(2000m, 500m, 0m, new DateOnly(2026, 7, 1));

        Assert.Equal(1500m, r.RemainingThisMonth);
        Assert.Equal(31, r.DaysLeftInMonth);
        Assert.Equal(48.38m, r.SafeToSpendToday);
    }

    [Fact]
    public void Last_day_of_month_gives_whole_remaining()
    {
        var r = SafeToSpendCalculator.Calculate(1000m, 700m, 0m, new DateOnly(2026, 7, 31));

        Assert.Equal(1, r.DaysLeftInMonth);
        Assert.Equal(300m, r.SafeToSpendToday);
    }

    [Fact]
    public void Overspent_shows_negative_honestly()
    {
        var r = SafeToSpendCalculator.Calculate(1000m, 1200m, 0m, new DateOnly(2026, 7, 31));

        Assert.Equal(-200m, r.RemainingThisMonth);
        Assert.Equal(-200m, r.SafeToSpendToday);
    }

    [Fact]
    public void February_leap_year_day_count()
    {
        // 2028 is a leap year, February has 29 days. today 2028-02-20 -> 29 - 20 + 1 = 10
        var r = SafeToSpendCalculator.Calculate(1000m, 0m, 0m, new DateOnly(2028, 2, 20));

        Assert.Equal(10, r.DaysLeftInMonth);
        Assert.Equal(100m, r.SafeToSpendToday);
    }
}
