namespace FinanceApp.Domain.Budgeting;

/// What the user decided to do with the money left over when a period ended.
///
/// Until this existed, a leftover simply evaporated: the new period's budget is the new
/// income, so an underspent month showed up nowhere except the bank balance — and the app,
/// which asks to be trusted with one number, was quietly poorer than reality every single
/// month. The money is real; somebody has to say where it belongs.
///
/// One row per period start, whatever the answer was, so the question is asked exactly once.
/// A recorded <see cref="CarryoverDecision.Ignore"/> is not "nothing happened" — it is "asked
/// and answered", and it is the only thing that stops the card coming back.
public class PeriodCarryover
{
    public int Id { get; set; }

    /// First day of the period that INHERITS the money — the one running when the question is
    /// asked, not the one the money is left over from. Unique: the decision is per period.
    public DateOnly PeriodStart { get; set; }

    /// The leftover as it was computed at the moment of asking, in base currency. Frozen on
    /// purpose: the previous period's arithmetic can still shift afterwards (a forgotten
    /// receipt, an edited amount), and a jar deposit that silently changed size later would
    /// be money moving without anybody moving it.
    public decimal AmountBase { get; set; }

    public CarryoverDecision Decision { get; set; }

    /// The jar it went into, for <see cref="CarryoverDecision.ToEnvelope"/>. The deposit is an
    /// ordinary savings entry, so removing that entry is how the move is undone.
    public int? EnvelopeId { get; set; }

    public DateTimeOffset DecidedAt { get; set; }
}

public enum CarryoverDecision
{
    /// Moved into a jar: it stops being spendable and starts being saved.
    ToEnvelope,

    /// Added to this period's budget, so the daily norm gets to spend it.
    ToBudget,

    /// Deliberately left out of the app's arithmetic. The money exists in the bank; the user
    /// says it is not part of this period.
    Ignore,
}
