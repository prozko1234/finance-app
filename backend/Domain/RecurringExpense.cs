namespace FinanceApp.Domain;

/// A fixed expense that repeats on a schedule (subscription, rent, insurance, ...).
/// It is materialized into a Transaction on its due day, and reserved in
/// safe-to-spend until then — so the headline number never jumps when it charges.
public class RecurringExpense
{
    public int Id { get; set; }
    /// Expense (subscription) or Income (a stable monthly salary/contract).
    public TransactionKind Kind { get; set; } = TransactionKind.Expense;
    /// Income only: whether AmountOriginal already contains VAT. Ignored for expenses.
    public bool AmountIncludesVat { get; set; } = true;
    public decimal AmountOriginal { get; set; }
    public required string CurrencyOriginal { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    /// The first charge. Everything else is counted from here, which is why weekly schedules
    /// are possible at all — a day-of-month cannot say "every other Tuesday".
    /// For monthly and yearly rules this date's day is the day it lands on, clamped to short
    /// months (the 31st in February becomes the 28th, and is back to the 31st in March).
    public DateOnly StartsOn { get; set; }

    public RecurrenceUnit Unit { get; set; } = RecurrenceUnit.Month;

    /// Every <see cref="Interval"/> units. 2 + Week is a fortnight, 3 + Month is a quarter —
    /// which is why there is no Quarter unit.
    public int Interval { get; set; } = 1;

    /// Above this a schedule stops being a repeat and starts being a typo.
    public const int MaxInterval = 60;
    public bool Active { get; set; } = true;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
