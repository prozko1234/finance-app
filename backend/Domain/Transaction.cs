namespace FinanceApp.Domain;

public class Transaction
{
    public int Id { get; set; }

    // --- Сума як введено ---
    public decimal AmountOriginal { get; set; }
    public required string CurrencyOriginal { get; set; }

    // --- Сума в базовій валюті (PLN). Фіксується при створенні, НІКОЛИ не перераховується. ---
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
