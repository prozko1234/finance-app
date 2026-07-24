namespace FinanceApp.Domain;

public class Transaction
{
    public int Id { get; set; }

    // --- Amount as entered ---
    public decimal AmountOriginal { get; set; }
    public required string CurrencyOriginal { get; set; }

    // --- Amount in base currency (PLN). Fixed at creation, NEVER recomputed. ---
    public decimal AmountBase { get; set; }
    public decimal FxRate { get; set; }
    public DateOnly FxDate { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public Priority Priority { get; set; }
    public Frequency Frequency { get; set; }
    public TxSource Source { get; set; }

    public DateOnly Date { get; set; }
    public string? MerchantRaw { get; set; }
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
