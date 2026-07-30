namespace FinanceApp.Domain.Savings;

/// <param name="Remaining">What is still missing, never negative.</param>
/// <param name="PeriodsLeft">Budget periods the target still has, counting the one being lived
/// in. Zero only when there is no date at all.</param>
/// <param name="PerPeriod">What has to go in each remaining period to arrive on time — the
/// whole point of setting a date. Zero when there is no date, or when the target is met.</param>
public record TargetPace(
    decimal Remaining, int PeriodsLeft, decimal PerPeriod, bool Reached, bool Overdue);

/// Turns «6 000 до червня» into «950 за період». Periods rather than calendar months, like
/// everything else in the app: someone paid on the 10th lives in 10.07–09.08, and a figure per
/// calendar month would not line up with the money actually arriving.
///
/// The pace is INFORMATION, not a reservation: nothing here touches safe-to-spend. A target
/// that quietly held money back would compete with the allocation scheme for the same money and
/// hold it twice — and the app would be deciding for the user what a wish costs them today.
/// Pure function — no DB, no clock.
public static class EnvelopeTargets
{
    /// <param name="periodsLeft">How many periods the date still leaves, the current one
    /// included — or null when the target has no date and therefore no pace. Zero or less means
    /// the date has gone by.</param>
    public static TargetPace Pace(decimal target, decimal balance, int? periodsLeft)
    {
        var remaining = Math.Max(0m, target - balance);

        if (remaining == 0m)
            return new TargetPace(0m, Math.Max(0, periodsLeft ?? 0), 0m, true, false);

        // No date, no pace: «зібрати 6 000» is a goal, and inventing a deadline for it would
        // be the app deciding when the user's wish is due.
        if (periodsLeft is null) return new TargetPace(remaining, 0, 0m, false, false);

        // A date already gone is not hidden: what is still missing becomes what to put in now,
        // because now is all that is left.
        if (periodsLeft <= 0) return new TargetPace(remaining, 0, remaining, false, true);

        // Rounded UP: at 2 decimals down, the last period would come out short of the target
        // by a few groszy, and a plan that misses its own goal is not a plan.
        var perPeriod = Math.Ceiling(remaining / periodsLeft.Value * 100m) / 100m;
        return new TargetPace(remaining, periodsLeft.Value, perPeriod, false, false);
    }
}
