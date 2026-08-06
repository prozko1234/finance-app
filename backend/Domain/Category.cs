namespace FinanceApp.Domain;

public class Category : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }
    public required string Name { get; set; }
    public string? Icon { get; set; }
    /// Tailwind-ish hex color for the chip, e.g. "#059669". Optional.
    public string? Color { get; set; }
    public int SortOrder { get; set; }
    /// The fallback category ("Інше"): cannot be deleted, receives orphaned transactions.
    public bool IsSystem { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
