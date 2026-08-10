namespace FinanceApp.Domain.Budgeting;

public record SafeToSpendResult(
    bool BudgetSet,
    decimal? PeriodBudget,
    decimal SpentThisPeriod,
    decimal ReservedRecurring,
    decimal? RemainingThisPeriod,
    int DaysLeftInPeriod,
    decimal? DailyNorm,          // норма на сьогодні, зафіксована на початок дня
    decimal SpentToday,
    decimal? LeftToday,          // скільки з норми ще лишилось; від'ємне = перебір
    decimal? TomorrowIfStop,     // завтрашня норма, якщо сьогодні більше не витрачати
    decimal? TomorrowIfOnPlan,   // якою вона була б, якби сьогодні витратив рівно норму
    int DaysThisWeek,            // скільки днів покриває тижневе вікно — 7 або менше
    decimal? LeftThisWeek);      // скільки лишилось на це вікно, з урахуванням витраченого сьогодні

/// The core of the product: "how much is safe to spend today".
/// v1.2: the daily norm is fixed at the START of the day — (budget - spent before today -
/// not-yet-charged recurring) / days left — and today's spending is measured against it.
/// Deriving the norm from what is left RIGHT NOW makes it drift down with every purchase,
/// and then "over the norm" can never be said out loud: the norm has already moved to match.
/// Recurring are reserved up front, so the number does not jump when a subscription charges.
/// Pure function — fully testable.
public static class SafeToSpendCalculator
{
    public static SafeToSpendResult Calculate(
        decimal? periodBudget, decimal spentThisPeriod, decimal spentToday,
        decimal reservedRecurring, DateOnly today, BudgetPeriod period)
    {
        // Days to the next payday, not to the 1st: the money has to last until it arrives
        // again, and that is rarely the end of a calendar month.
        var daysLeft = period.DaysLeftFrom(today); // including today

        // The week is a window starting today, cut short when the period ends first — a figure
        // for "наступні 7 днів" must never promise money that arrives with the next payday.
        var daysThisWeek = Math.Min(WeekDays, daysLeft);

        if (periodBudget is null)
            return new SafeToSpendResult(
                false, null, spentThisPeriod, reservedRecurring, null, daysLeft,
                null, spentToday, null, null, null, daysThisWeek, null);

        var remaining = periodBudget.Value - spentThisPeriod - reservedRecurring;
        var remainingAtStartOfDay = remaining + spentToday;

        var dailyNorm = FloorTo2(remainingAtStartOfDay / daysLeft);
        var leftToday = dailyNorm - spentToday;

        // On the last day of the period there is no tomorrow to project onto.
        decimal? tomorrowIfStop = null, tomorrowIfOnPlan = null;
        if (daysLeft > 1)
        {
            tomorrowIfStop = FloorTo2(remaining / (daysLeft - 1));
            tomorrowIfOnPlan = FloorTo2((remainingAtStartOfDay - dailyNorm) / (daysLeft - 1));
        }

        // The same money over a longer horizon, not a second budget: keep to the norm every
        // day and this is what the window gives you. Today's spending is already off it,
        // because the window starts today.
        //
        // Once the window covers the whole period it IS the period, and the answer is
        // `remaining` rather than the multiplication — the norm is floored, so seven floored
        // days would come out a few groszy under the figure sitting beside it on the same
        // screen, and two numbers that should agree failing to is worse than either.
        var leftThisWeek = daysThisWeek == daysLeft
            ? remaining
            : dailyNorm * daysThisWeek - spentToday;

        return new SafeToSpendResult(
            true, periodBudget, spentThisPeriod, reservedRecurring, remaining, daysLeft,
            dailyNorm, spentToday, leftToday, tomorrowIfStop, tomorrowIfOnPlan,
            daysThisWeek, leftThisWeek);
    }

    /// A week is seven days from today, not "since Monday". Monday is the calendar's idea of a
    /// week; the question being answered is "скільки я можу витратити найближчим часом", and
    /// that does not reset on a particular morning.
    private const int WeekDays = 7;

    // Round money DOWN to 2 decimals — a "safe" figure must not promise too much.
    private static decimal FloorTo2(decimal v) => Math.Floor(v * 100m) / 100m;
}
