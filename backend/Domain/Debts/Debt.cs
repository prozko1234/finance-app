namespace FinanceApp.Domain.Debts;

/// Which way the money owes. Kept as a direction on one entity rather than two tables: a debt
/// and a loan are the same three facts — a person, an amount, and what has been paid off — and
/// splitting them would mean writing the arithmetic twice and getting it wrong once.
public enum DebtDirection
{
    /// Money the user has to give back.
    IOwe,

    /// Money somebody has to give back to the user.
    TheyOweMe,
}

/// One debt, either way round.
///
/// Envelopes could not do this job. A debt envelope differed from a savings one only by the
/// label on the button — «Погасити» instead of «Відкласти» — while the mechanics were the
/// same: money goes in and the balance grows. For a debt that reads backwards, which is
/// exactly why it "дивно працює": paying a debt off makes it SMALLER, and there was no
/// number on the screen doing that.
///
/// Money is stored the way it is everywhere else — what was typed, the base amount, and the
/// rate it was converted at — so a debt taken in euro keeps saying euro.
public class Debt : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    public DebtDirection Direction { get; set; }

    /// Whoever is on the other side. Free text on purpose: these are brothers, landlords and
    /// friends, not entities the app should ask the user to create first.
    public string Person { get; set; } = "";

    /// Always positive; <see cref="Direction"/> carries which way it points.
    public decimal AmountOriginal { get; set; }
    public string CurrencyOriginal { get; set; } = Money.BaseCurrency;
    public decimal AmountBase { get; set; }
    /// Fixed when the debt was written down, never recomputed.
    public decimal FxRate { get; set; } = 1m;
    public DateOnly FxDate { get; set; }

    /// When the money changed hands — not when the row was typed.
    public DateOnly Date { get; set; }

    /// Which pocket the money came out of when it was lent — or went into when it was
    /// borrowed. Without it a debt was a note rather than a movement: lending 500 zł left the
    /// daily norm untouched, and then the 500 coming back was ADDED to the budget, so the app
    /// invented money every time somebody paid the user back.
    ///
    /// <see cref="MoneySource.AlreadyHappened"/> is the migration's default, and the honest
    /// answer for every debt written down before this existed: the money moved before the app
    /// was told, so nothing in this period pays for it.
    public MoneySource Origin { get; set; } = MoneySource.AlreadyHappened;

    /// Which jar the money was lent out of, when <see cref="Origin"/> says Envelope. Null
    /// otherwise. Only ever set on a debt the user is owed — money arriving does not come out
    /// of a pot, the same rule <see cref="DebtPayment"/> already follows.
    public int? OriginEnvelopeId { get; set; }
    public Savings.Envelope? OriginEnvelope { get; set; }

    /// The day it is meant to be settled by, if anybody said. Optional: «віддам як зможу» is
    /// most of them, and demanding a date would make the app refuse to record real life.
    public DateOnly? Deadline { get; set; }

    /// Hold a share of this debt back from the daily norm every period until the deadline,
    /// the way a jar with a target does.
    ///
    /// Off by default, and per debt rather than a global setting. Always reserving would drop
    /// the norm through the floor the moment an old debt was entered — money the user is not
    /// paying back this month is not money missing from this month. Never reserving is unfair
    /// the other way: a debt due in three weeks IS a claim on today's money, and the app
    /// staying silent about it is how the deadline arrives with nothing set aside.
    ///
    /// Only meaningful with a deadline: without one there is nothing to divide by.
    public bool ReserveFromBudget { get; set; }

    /// Set by hand when the user calls it done, whatever the arithmetic says — debts get
    /// forgiven, rounded off and written off, and the app should not argue. Null while open.
    public DateOnly? ClosedOn { get; set; }

    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
