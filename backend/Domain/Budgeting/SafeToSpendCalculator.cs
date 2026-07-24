namespace FinanceApp.Domain.Budgeting;

public record SafeToSpendResult(
    bool BudgetSet,
    decimal? MonthlyBudget,
    decimal SpentThisMonth,
    decimal ReservedRecurring,
    decimal? RemainingThisMonth,
    int DaysLeftInMonth,
    decimal? SafeToSpendToday);

/// The core of the product: "how much is safe to spend today".
/// v1.1 formula: (budget - spent this month - not-yet-charged recurring) / days left.
/// Recurring are reserved up front, so the number does not jump when a subscription charges
/// (the reserved amount simply moves into "spent"). Pure function — fully testable.
public static class SafeToSpendCalculator
{
    public static SafeToSpendResult Calculate(
        decimal? monthlyBudget, decimal spentThisMonth, decimal reservedRecurring, DateOnly today)
    {
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var daysLeft = daysInMonth - today.Day + 1; // including today

        if (monthlyBudget is null)
            return new SafeToSpendResult(false, null, spentThisMonth, reservedRecurring, null, daysLeft, null);

        var remaining = monthlyBudget.Value - spentThisMonth - reservedRecurring;
        var perDay = FloorTo2(remaining / daysLeft);
        return new SafeToSpendResult(
            true, monthlyBudget, spentThisMonth, reservedRecurring, remaining, daysLeft, perDay);
    }

    // Round money DOWN to 2 decimals — a "safe" figure must not promise too much.
    private static decimal FloorTo2(decimal v) => Math.Floor(v * 100m) / 100m;
}
