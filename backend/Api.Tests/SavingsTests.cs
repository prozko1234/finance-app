using FinanceApp.Domain.Savings;

namespace FinanceApp.Api.Tests;

/// The savings envelope has one rule that is easy to get wrong: the monthly goal and a
/// manual deposit must never both reduce safe-to-spend for the same money.
public class SavingsTests
{
    private static SavingsPlan Fixed(decimal amount) =>
        new() { Mode = SavingsMode.Fixed, Value = amount, Active = true };

    [Fact]
    public void Goal_is_reserved_before_anything_is_actually_moved()
    {
        var s = SavingsCalculator.Status(Fixed(2000m), monthlyTakeHome: 15_000m, balance: 0m, depositedThisMonth: 0m);

        Assert.Equal(2000m, s.MonthGoal);
        Assert.Equal(2000m, s.StillToReserve);
        Assert.Equal(0m, s.Balance);
    }

    [Fact]
    public void Manual_deposit_eats_into_the_reservation_instead_of_stacking_on_it()
    {
        // Key property: 500 moved by hand does not cost another 500 of safe-to-spend.
        var s = SavingsCalculator.Status(Fixed(2000m), 15_000m, balance: 500m, depositedThisMonth: 500m);

        Assert.Equal(1500m, s.StillToReserve);
        Assert.Equal(2000m, s.DepositedThisMonth + s.StillToReserve); // total impact unchanged
        Assert.Equal(500m, s.Balance);
    }

    [Fact]
    public void Depositing_beyond_the_goal_never_produces_a_negative_reservation()
    {
        var s = SavingsCalculator.Status(Fixed(2000m), 15_000m, balance: 3000m, depositedThisMonth: 3000m);

        Assert.Equal(0m, s.StillToReserve); // not -1000: extra saving is a choice, not a refund
        Assert.Equal(3000m, s.Balance);
    }

    [Fact]
    public void Balance_survives_months_but_the_goal_resets()
    {
        // Last month's 2000 is still in the envelope; this month starts owing the goal again.
        var s = SavingsCalculator.Status(Fixed(2000m), 15_000m, balance: 2000m, depositedThisMonth: 0m);

        Assert.Equal(2000m, s.Balance);
        Assert.Equal(2000m, s.StillToReserve);
    }

    [Fact]
    public void Percent_goal_is_taken_from_take_home_not_from_revenue()
    {
        var plan = new SavingsPlan { Mode = SavingsMode.Percent, Value = 10m, Active = true };

        var s = SavingsCalculator.Status(plan, monthlyTakeHome: 15_245m, balance: 0m, depositedThisMonth: 0m);

        Assert.Equal(1524.50m, s.MonthGoal);
    }

    [Fact]
    public void Percent_goal_is_zero_until_there_is_income()
    {
        var plan = new SavingsPlan { Mode = SavingsMode.Percent, Value = 10m, Active = true };

        var s = SavingsCalculator.Status(plan, monthlyTakeHome: 0m, balance: 0m, depositedThisMonth: 0m);

        Assert.Equal(0m, s.MonthGoal); // never invent a goal out of income that does not exist
    }

    [Fact]
    public void No_plan_reserves_nothing_but_still_reports_the_balance()
    {
        var s = SavingsCalculator.Status(plan: null, monthlyTakeHome: 15_000m, balance: 800m, depositedThisMonth: 0m);

        Assert.Equal(0m, s.MonthGoal);
        Assert.Equal(0m, s.StillToReserve);
        Assert.Equal(800m, s.Balance);
    }

    [Fact]
    public void Inactive_plan_stops_reserving_without_losing_the_balance()
    {
        var plan = Fixed(2000m);
        plan.Active = false;

        var s = SavingsCalculator.Status(plan, 15_000m, balance: 4000m, depositedThisMonth: 0m);

        Assert.Equal(0m, s.StillToReserve);
        Assert.Equal(4000m, s.Balance);
    }
}
