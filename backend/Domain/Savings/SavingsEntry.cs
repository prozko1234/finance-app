namespace FinanceApp.Domain.Savings;

using FinanceApp.Domain;

public enum SavingsEntryKind { Deposit, Withdrawal }

/// One real movement in or out of an envelope. The balance is the sum of these — never a
/// stored figure, so it can always be reconstructed and audited.
///
/// Still called SavingsEntry: the table predates envelopes and renaming it would rebuild
/// it on SQLite for no behavioural gain. <see cref="EnvelopeId"/> is what makes it general.
///
/// Money is stored exactly as on a transaction: what the user typed, plus the base amount
/// and the rate it was converted at. Someone saving in USD wants to see the USD they put
/// in, while the balance and the monthly goal can only work in one currency.
public class SavingsEntry : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    /// Which pot this movement belongs to.
    public int EnvelopeId { get; set; }
    public Envelope? Envelope { get; set; }

    public DateOnly Date { get; set; }
    public SavingsEntryKind Kind { get; set; }

    /// Always positive; Kind carries the direction.
    public decimal AmountOriginal { get; set; }
    public string CurrencyOriginal { get; set; } = Money.BaseCurrency;
    /// The movement in base currency — balance and goal are built from this.
    public decimal AmountBase { get; set; }
    /// Fixed at entry time, never recomputed: an old deposit keeps the rate it was made at.
    public decimal FxRate { get; set; } = 1m;
    public DateOnly FxDate { get; set; }

    /// Written by the app itself to carry out the allocation scheme, rather than by the user
    /// moving money by hand. There is at most one per envelope per period, and the app keeps
    /// its amount in step with the goal — so a budget that grows or shrinks mid-period does
    /// not leave a trail of correcting deposits.
    public bool IsAuto { get; set; }

    /// Money that was ALREADY set aside before it was written down — an old pot being entered
    /// so the balance is right, a gift, last year's bonus. It joins the balance and never
    /// touches the period's budget.
    ///
    /// Without this the app has to assume every deposit is money leaving the current budget
    /// right now, and typing in what a jar already held took that amount straight off "скільки
    /// можна витратити": entering a pot of 7 000 that had been saved over a year read as
    /// spending 7 000 today, and the daily figure went deeply negative for money that had
    /// never been in this period's income to begin with.
    ///
    /// A date is not enough to tell these apart. Money moved months ago can be written down
    /// today, and money moved today can come from anywhere — only the person entering it
    /// knows which, so the form asks.
    public bool AlreadySetAside { get; set; }

    /// Ties the two halves of a move between jars: the withdrawal from one and the deposit
    /// into the other carry the same key. They are one act, so they are undone as one — half a
    /// transfer left behind would make «Відкладено всього» grow by money nobody received.
    /// Null for an ordinary movement, which is almost all of them.
    public string? TransferKey { get; set; }

    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
