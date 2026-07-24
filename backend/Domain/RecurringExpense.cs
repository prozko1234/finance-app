namespace FinanceApp.Domain;

/// A fixed expense that repeats every month (subscription, rent, ...).
/// It is materialized into a Transaction on its due day, and reserved in
/// safe-to-spend until then — so the headline number never jumps when it charges.
public class RecurringExpense
{
    public int Id { get; set; }
    public decimal AmountOriginal { get; set; }
    public required string CurrencyOriginal { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public int DayOfMonth { get; set; }   // 1..31, clamped to the month length
    public bool Active { get; set; } = true;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
