namespace FinanceApp.Domain.Budgeting;

/// When a recurring charge falls due. Every occurrence is computed from the anchor date
/// rather than from the previous one: stepping forward a date at a time lets a short month
/// bend the whole series — rent on the 31st slips to the 28th in February and then stays
/// there for good — and that bug costs a real payment.
public static class RecurringSchedule
{
    /// Stops a corrupt interval from turning an enumeration into an endless loop. No real
    /// schedule produces this many dates inside the windows this is ever asked about.
    private const int MaxOccurrences = 5_000;

    /// The occurrence <paramref name="steps"/> periods after the anchor. AddMonths and
    /// AddYears clamp to the length of the target month themselves, which is exactly what
    /// the 31st and the 29th of February need.
    public static DateOnly Advance(DateOnly anchor, RecurrenceUnit unit, int interval, int steps) =>
        unit switch
        {
            RecurrenceUnit.Week => anchor.AddDays(7 * interval * steps),
            RecurrenceUnit.Month => anchor.AddMonths(interval * steps),
            RecurrenceUnit.Year => anchor.AddYears(interval * steps),
            _ => anchor,
        };

    /// Every occurrence inside [from, to], inclusive. Empty when the window is backwards or
    /// ends before the schedule starts.
    public static IEnumerable<DateOnly> Occurrences(
        DateOnly anchor, RecurrenceUnit unit, int interval, DateOnly from, DateOnly to)
    {
        if (interval < 1 || to < from || to < anchor) yield break;

        // Start counting near the window rather than walking from the anchor: a weekly
        // subscription started years ago would otherwise take hundreds of steps to answer a
        // question about one month. Stepping back one covers the rounding.
        var step = Math.Max(0, StepsBefore(anchor, unit, interval, from) - 1);

        for (var i = 0; i < MaxOccurrences; i++, step++)
        {
            var occ = Advance(anchor, unit, interval, step);
            if (occ > to) yield break;
            if (occ >= from) yield return occ;
        }
    }

    /// The first occurrence strictly after <paramref name="after"/>, or null when the
    /// schedule does not reach one inside <paramref name="within"/> days. The bound is what
    /// keeps "when next?" from scanning forever on a yearly row.
    public static DateOnly? NextAfter(
        DateOnly anchor, RecurrenceUnit unit, int interval, DateOnly after, int within = 800)
    {
        foreach (var occ in Occurrences(anchor, unit, interval, after.AddDays(1), after.AddDays(within)))
            return occ;

        return null;
    }

    /// Roughly how many whole periods fit between the anchor and the target. Deliberately
    /// approximate — the caller steps back one and scans forward, so being off by one is
    /// cheaper than being exact.
    private static int StepsBefore(DateOnly anchor, RecurrenceUnit unit, int interval, DateOnly target)
    {
        var elapsed = unit switch
        {
            RecurrenceUnit.Week => (target.DayNumber - anchor.DayNumber) / 7,
            RecurrenceUnit.Month => ((target.Year - anchor.Year) * 12) + target.Month - anchor.Month,
            RecurrenceUnit.Year => target.Year - anchor.Year,
            _ => 0,
        };

        // Floor division — C# truncates towards zero, and a target before the anchor is
        // exactly the case this is asked about.
        return (int)Math.Floor(elapsed / (double)interval);
    }
}
