namespace FinanceApp.Domain.Budgeting;

/// What a standing charge costs per month, whatever rhythm it actually runs on. A yearly domain
/// and a weekly cleaner are the same kind of cost, and the only way to compare them — or to
/// answer «скільки в мене йде на підписки» — is to put them on one scale.
public static class RecurringCost
{
    /// Weeks go through the average year (365.25 / 7 / 12 ≈ 4.348 weeks a month) rather than
    /// through "four weeks": four would under-count a weekly charge by a whole month's worth
    /// every year.
    public static decimal PerMonth(decimal amount, RecurrenceUnit unit, int interval)
    {
        var every = Math.Max(interval, 1);

        return unit switch
        {
            RecurrenceUnit.Week => amount * 365.25m / 7m / 12m / every,
            RecurrenceUnit.Year => amount / 12m / every,
            _ => amount / every,
        };
    }
}
