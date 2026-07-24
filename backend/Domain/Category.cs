namespace FinanceApp.Domain;

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Icon { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
