namespace FinanceApp.Domain;

public class Category : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }
    public required string Name { get; set; }

    /// Which side of the ledger this belongs to. Everything written before income had
    /// categories of its own is an expense one, which is what the column's default says.
    public CategoryKind Kind { get; set; } = CategoryKind.Expense;
    public string? Icon { get; set; }
    /// Tailwind-ish hex color for the chip, e.g. "#059669". Optional.
    public string? Color { get; set; }
    public int SortOrder { get; set; }
    /// The fallback category ("Інше"): cannot be deleted, receives orphaned transactions.
    /// There is one per <see cref="Kind"/> — moving a salary into the expense "Інше" would
    /// put income in a list that only ever sums spending.
    public bool IsSystem { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
