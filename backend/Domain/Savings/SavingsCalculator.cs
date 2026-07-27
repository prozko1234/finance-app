namespace FinanceApp.Domain.Savings;

public record SavingsStatus(
    decimal Balance,           // накопичено всього
    decimal MonthGoal,         // ціль на цей місяць
    decimal DepositedThisMonth,
    decimal StillToReserve);   // ще не відкладено з цілі — саме це ховається від safe-to-spend

/// Keeps manual deposits and the monthly goal from being counted twice.
/// The goal reserves money up front; every manual deposit this month eats into that
/// reservation instead of adding to it. Either way the same amount leaves safe-to-spend.
/// Pure function — no DB, no clock.
public static class SavingsCalculator
{
    public static SavingsStatus Status(
        SavingsPlan? plan, decimal monthlyTakeHome, decimal balance, decimal depositedThisMonth) =>
        Status(MonthGoal(plan, monthlyTakeHome), balance, depositedThisMonth);

    /// Same rules for a goal that comes from elsewhere — an allocation scheme's Savings
    /// bucket. Deposits eat into the reservation there too, so the money is never held twice.
    public static SavingsStatus Status(decimal goal, decimal balance, decimal depositedThisMonth)
    {
        var stillToReserve = Math.Max(0m, goal - depositedThisMonth);
        return new SavingsStatus(balance, goal, depositedThisMonth, stillToReserve);
    }

    public static decimal MonthGoal(SavingsPlan? plan, decimal monthlyTakeHome)
    {
        if (plan is null || !plan.Active || plan.Value <= 0) return 0m;

        return plan.Mode switch
        {
            SavingsMode.Fixed => plan.Value,
            // Percent of take-home: no income yet means no goal yet — never invent one.
            SavingsMode.Percent => Math.Round(
                Math.Max(0m, monthlyTakeHome) * plan.Value / 100m, 2, MidpointRounding.AwayFromZero),
            _ => 0m,
        };
    }
}
