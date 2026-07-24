namespace FinanceApp.Domain.Budgeting;

public record SafeToSpendResult(
    bool BudgetSet,
    decimal? MonthlyBudget,
    decimal SpentThisMonth,
    decimal? RemainingThisMonth,
    int DaysLeftInMonth,
    decimal? SafeToSpendToday);

/// Ядро продукту: «скільки безпечно витратити сьогодні».
/// Формула v1: (місячний бюджет − витрачено за місяць) / кількість днів до кінця місяця (включно з сьогодні).
/// Чиста функція без БД — тому легко й повно тестується.
public static class SafeToSpendCalculator
{
    public static SafeToSpendResult Calculate(decimal? monthlyBudget, decimal spentThisMonth, DateOnly today)
    {
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var daysLeft = daysInMonth - today.Day + 1; // включно з сьогодні

        if (monthlyBudget is null)
            return new SafeToSpendResult(false, null, spentThisMonth, null, daysLeft, null);

        var remaining = monthlyBudget.Value - spentThisMonth;
        var perDay = FloorTo2(remaining / daysLeft);
        return new SafeToSpendResult(true, monthlyBudget, spentThisMonth, remaining, daysLeft, perDay);
    }

    // Округлюємо ВНИЗ до 2 знаків — «безпечна» цифра не має обіцяти зайвого.
    private static decimal FloorTo2(decimal v) => Math.Floor(v * 100m) / 100m;
}
