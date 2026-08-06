namespace FinanceApp.Domain;

public class Transaction : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

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

    /// Which envelope this was paid out of. Null — the ordinary case — means the money
    /// came from what is free to spend, and the expense pushes the daily norm down.
    ///
    /// Replaced Priority (треба/варто/хочу), which asked for a decision on every single
    /// entry and then did nothing with the answer: no number on any screen changed because
    /// a purchase was a "want". Where the money comes from does change one — the envelope
    /// it leaves was already held back, so spending it must not be counted twice.
    public int? EnvelopeId { get; set; }
    public Savings.Envelope? Envelope { get; set; }

    public Frequency Frequency { get; set; }
    public TxSource Source { get; set; }

    public DateOnly Date { get; set; }
    public string? MerchantRaw { get; set; }
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
