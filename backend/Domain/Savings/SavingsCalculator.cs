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
    /// <param name="pouredByScheme">What the app itself has already moved into this jar for
    /// the period. Zero when nothing was poured, which is what the manual plan passes.</param>
    public static SavingsStatus Status(
        decimal goal, decimal balance, decimal depositedThisMonth, decimal pouredByScheme = 0m)
    {
        // Measured against what has actually been MOVED, not against what is left over after
        // the user took some back out.
        //
        // `depositedThisMonth` is a NET figure, so a withdrawal pushes it down — and reserving
        // the difference again pinned the total held at the goal no matter what. Taking money
        // out of a jar then freed nothing: the app stopped calling it "saved" and went on
        // hiding it from the daily norm, so the one lever the user has over their own plan did
        // nothing at all, silently. That is the opposite of what
        // <see cref="FinanceApp.Application.Envelopes.EnvelopeService"/> promises when it
        // deliberately leaves withdrawals alone rather than refilling them.
        //
        // Once the scheme has poured the goal, the goal is met. What happens to that money
        // afterwards is a decision, and decisions are allowed to have consequences.
        var moved = Math.Max(depositedThisMonth, pouredByScheme);
        var stillToReserve = Math.Max(0m, goal - moved);
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
