namespace FinanceApp.Domain;

/// App-wide preferences. MVP — a single row, like Budget.
public class AppSettings
{
    public int Id { get; set; }

    /// The currency the user reads everything in. Storage never leaves
    /// <see cref="Money.BaseCurrency"/>: this decides how stored amounts are shown, not
    /// how they are kept, so switching it rewrites nothing and loses no history.
    public string DisplayCurrency { get; set; } = Money.BaseCurrency;

    /// The day of the month money arrives — the day a budget period starts
    /// (<see cref="Budgeting.BudgetPeriods"/>). 1 keeps the old calendar-month behaviour,
    /// which is why it is the default: an existing database reads the same as before.
    public int PeriodStartDay { get; set; } = Budgeting.BudgetPeriods.FirstOfMonth;

    public DateTimeOffset UpdatedAt { get; set; }
}
