namespace FinanceApp.Domain;

/// A fixed expense that repeats on a schedule (subscription, rent, insurance, ...).
/// It is materialized into a Transaction on its due day, and reserved in
/// safe-to-spend until then — so the headline number never jumps when it charges.
public class RecurringExpense : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }
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

    /// The shop this charge shows up as on a bank statement, as
    /// <see cref="Import.MerchantKey"/> keys it — "ANTHROPIC", "SYLWIA".
    ///
    /// Learned on import, the same way a category is: the user says once that a statement row
    /// IS this subscription, and every later statement recognises it without being asked. Null
    /// until then, and null forever for anything never imported.
    ///
    /// It exists because the amount cannot do this job. The whole reason to match a statement
    /// row against a subscription is that the price may have changed — Claude went from 99,16
    /// to 502,67 in one month — so matching on the figure would miss exactly the cases worth
    /// catching, and the app would write the charge twice.
    public string? MerchantKey { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
