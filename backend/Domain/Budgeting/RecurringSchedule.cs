namespace FinanceApp.Domain.Budgeting;

public static class RecurringSchedule
{
    /// The date a monthly recurring charge falls on in a given month.
    /// Day is clamped to the month length (e.g. day 31 in February -> 28/29).
    public static DateOnly OccurrenceDate(int year, int month, int dayOfMonth)
    {
        var day = Math.Clamp(dayOfMonth, 1, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }
}
