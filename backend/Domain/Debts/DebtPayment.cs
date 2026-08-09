namespace FinanceApp.Domain.Debts;

/// Where the money for this payment came from — or, for money coming back, where it went.
///
/// This is the whole feature. A payment on a debt is not a new kind of movement, it is an
/// ordinary one with a source, and the app already knows all three sources: it just never had
/// to name them in one place before.
public enum DebtPaymentSource
{
    /// Out of (or into) what is free to spend right now. The daily norm moves.
    Spendable,

    /// Out of a jar. The norm does not move: that money was held back when it went into the
    /// jar, and charging for it again would take the same złoty twice — the mistake
    /// <see cref="Savings.SavingsEntry.AlreadySetAside"/> was written to stop.
    Envelope,

    /// Money that moved before it was written down. Nothing this period pays for it, exactly
    /// like a deposit marked as already set aside. A date cannot tell this apart from the
    /// others — money moved months ago gets typed in today — so the form asks.
    AlreadyHappened,
}

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

    public DebtPaymentSource Source { get; set; }

    /// Which jar it came out of, when <see cref="Source"/> says Envelope. Null otherwise.
    public int? EnvelopeId { get; set; }
    public Savings.Envelope? Envelope { get; set; }

    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
