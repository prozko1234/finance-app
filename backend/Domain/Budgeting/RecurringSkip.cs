namespace FinanceApp.Domain.Budgeting;

/// A charge that this recurring rule was NOT supposed to make after all.
///
/// Materialization is idempotent by asking "is there already a transaction for this rule on
/// this date?" — which is exactly the evidence a deletion destroys. Without a record of the
/// deletion, the very next read wrote the charge again, and deleting a subscription's expense
/// looked like the app arguing with the user.
///
/// So the fact that it was deleted has to be stored somewhere the deletion cannot erase.
public class RecurringSkip
{
    public int Id { get; set; }

    public int RecurringExpenseId { get; set; }
    public RecurringExpense? RecurringExpense { get; set; }

    /// The occurrence that must not come back.
    public DateOnly Date { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
