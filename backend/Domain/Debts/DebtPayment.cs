namespace FinanceApp.Domain.Debts;

/// One movement against a debt: a repayment, or money coming back.
///
/// The balance of a debt is the sum of these, never a stored figure — the same rule the
/// envelopes follow, and for the same reason: a total that is recomputed can be audited, and
/// a total that is written down drifts.
public class DebtPayment : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    public int DebtId { get; set; }
    public Debt? Debt { get; set; }

    public DateOnly Date { get; set; }

    /// Always positive. Which way it moves is the debt's direction, not this row's business.
    public decimal AmountOriginal { get; set; }
    public string CurrencyOriginal { get; set; } = Money.BaseCurrency;
    public decimal AmountBase { get; set; }
    public decimal FxRate { get; set; } = 1m;
    public DateOnly FxDate { get; set; }

    public MoneySource Source { get; set; }

    /// Which jar it came out of, when <see cref="Source"/> says Envelope. Null otherwise.
    public int? EnvelopeId { get; set; }
    public Savings.Envelope? Envelope { get; set; }

    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
