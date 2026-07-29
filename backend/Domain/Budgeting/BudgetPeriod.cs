namespace FinanceApp.Domain.Budgeting;

/// The stretch of days one budget covers: from the day money arrives to the day before it
/// arrives again.
///
/// The app used to call this "the calendar month", which is only right for people paid on
/// the 1st. Paid on the 10th, you spend the last days of a month on money that has already
/// run out on paper, and then the norm jumps on the 1st while your account is still empty.
public readonly record struct BudgetPeriod(DateOnly Start, DateOnly End)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// Days still to cover, today included. Never zero: dividing what is left by the days
    /// left has to stay meaningful on the last day.
    public int DaysLeftFrom(DateOnly today) => Math.Max(1, End.DayNumber - today.DayNumber + 1);

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

public static class BudgetPeriods
{
    /// What the app assumed before the day was configurable — and still the default, since
    /// it is what someone with no strong payday would expect.
    public const int FirstOfMonth = 1;

    /// The period <paramref name="date"/> falls in, for a payday on
    /// <paramref name="startDay"/>. A day past the end of a short month is pulled back to
    /// that month's last day, the same way a recurring charge on the 31st is
    /// (<see cref="RecurringSchedule"/>) — periods must tile the calendar with no gap and
    /// no overlap, so the day cannot simply be skipped.
    public static BudgetPeriod For(DateOnly date, int startDay)
    {
        startDay = Math.Clamp(startDay, 1, 31);

        var start = StartIn(date.Year, date.Month, startDay);
        if (date < start)
        {
            var previous = new DateOnly(date.Year, date.Month, 1).AddMonths(-1);
            start = StartIn(previous.Year, previous.Month, startDay);
        }

        // Taken from the month, not by adding a month to a clamped date: 31 January + 1
        // month is 28 February, and the period after it would then start on the 28th
        // forever after.
        var following = new DateOnly(start.Year, start.Month, 1).AddMonths(1);
        var next = StartIn(following.Year, following.Month, startDay);

        return new BudgetPeriod(start, next.AddDays(-1));
    }

    private static DateOnly StartIn(int year, int month, int startDay) =>
        new(year, month, Math.Min(startDay, DateTime.DaysInMonth(year, month)));
}
