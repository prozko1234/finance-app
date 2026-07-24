namespace FinanceApp.Domain.Budgeting;

public record SafeToSpendResult(
    bool BudgetSet,
    decimal? MonthlyBudget,
    decimal SpentThisMonth,
    decimal? RemainingThisMonth,
    int DaysLeftInMonth,
    decimal? SafeToSpendToday);

/// The core of the product: "how much is safe to spend today".
/// v1 formula: (monthly budget - spent this month) / days left in month (including today).
/// Pure function without a DB — so it is easy and fully testable.
public static class SafeToSpendCalculator
{
    public static SafeToSpendResult Calculate(decimal? monthlyBudget, decimal spentThisMonth, DateOnly today)
    {
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var daysLeft = daysInMonth - today.Day + 1; // including today

        if (monthlyBudget is null)
            return new SafeToSpendResult(false, null, spentThisMonth, null, daysLeft, null);

        var remaining = monthlyBudget.Value - spentThisMonth;
        var perDay = FloorTo2(remaining / daysLeft);
        return new SafeToSpendResult(true, monthlyBudget, spentThisMonth, remaining, daysLeft, perDay);
    }

    // Round money DOWN to 2 decimals — a "safe" figure must not promise too much.
    private static decimal FloorTo2(decimal v) => Math.Floor(v * 100m) / 100m;
}
