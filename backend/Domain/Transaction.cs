namespace FinanceApp.Domain;

public class Transaction
{
    public int Id { get; set; }

    public TransactionKind Kind { get; set; } = TransactionKind.Expense;

    // --- Income only: VAT is transit money, never part of income ---
    // AmountBase for an income row holds the revenue (przychód, VAT excluded) in PLN.
    public decimal? GrossWithVat { get; set; }
    public decimal? VatAmount { get; set; }

    // --- Amount as entered ---
    public decimal AmountOriginal { get; set; }
    public required string CurrencyOriginal { get; set; }

    // --- Amount in base currency (PLN). Fixed at creation, NEVER recomputed. ---
    public decimal AmountBase { get; set; }
    public decimal FxRate { get; set; }
    public DateOnly FxDate { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    // Set when this transaction was auto-generated from a recurring expense.
    // Together with Date it makes materialization idempotent (one per month).
    public int? RecurringExpenseId { get; set; }
    public RecurringExpense? RecurringExpense { get; set; }

    public Priority Priority { get; set; }
    public Frequency Frequency { get; set; }
    public TxSource Source { get; set; }

    public DateOnly Date { get; set; }
    public string? MerchantRaw { get; set; }
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
