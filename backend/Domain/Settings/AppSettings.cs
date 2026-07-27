namespace FinanceApp.Domain;

/// App-wide preferences. MVP — a single row, like Budget.
public class AppSettings
{
    public int Id { get; set; }

    /// The currency the user reads everything in. Storage never leaves
    /// <see cref="Money.BaseCurrency"/>: this decides how stored amounts are shown, not
    /// how they are kept, so switching it rewrites nothing and loses no history.
    public string DisplayCurrency { get; set; } = Money.BaseCurrency;

    public DateTimeOffset UpdatedAt { get; set; }
}
