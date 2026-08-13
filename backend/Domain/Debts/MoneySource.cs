namespace FinanceApp.Domain.Debts;

/// Which pocket money came out of — or, for money arriving, which one it went into.
///
/// This is the whole of how debts touch the budget. Lending, borrowing, repaying and being
/// repaid are not four kinds of movement; they are one movement with a direction and a pocket,
/// and the app already knew all three pockets. It just never had to name them in one place.
public enum MoneySource
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
