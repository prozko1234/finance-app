namespace FinanceApp.Domain;

/// App-wide preferences. MVP — a single row, like Budget.
public class AppSettings : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    /// The currency the user reads everything in. Storage never leaves
    /// <see cref="Money.BaseCurrency"/>: this decides how stored amounts are shown, not
    /// how they are kept, so switching it rewrites nothing and loses no history.
    public string DisplayCurrency { get; set; } = Money.BaseCurrency;

    /// The day of the month money arrives — the day a budget period starts
    /// (<see cref="Budgeting.BudgetPeriods"/>). 1 keeps the old calendar-month behaviour,
    /// which is why it is the default: an existing database reads the same as before.
    public int PeriodStartDay { get; set; } = Budgeting.BudgetPeriods.FirstOfMonth;

    /// The hour of the day a reminder about today's charges is sent, 0–23 local time. Null —
    /// no reminders, which is the default and what every existing row reads as.
    ///
    /// An hour rather than a fixed time, because the only thing that matters is that it is an
    /// hour the phone is in a hand. Midnight is when the charge technically falls due and is
    /// exactly the wrong moment to say so: the notification is read the next morning with the
    /// rest of the night's noise, by which time the money has gone and there is nothing to
    /// decide.
    public int? ReminderHour { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
