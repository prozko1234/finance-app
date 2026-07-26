namespace FinanceApp.Application.Common;

/// The calendar month a date falls in. Shared so the summary and the income preview
/// can never disagree about what "this month" means.
public static class MonthRange
{
    public static (DateOnly First, DateOnly Last) Of(DateOnly date) => (
        new DateOnly(date.Year, date.Month, 1),
        new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)));
}
