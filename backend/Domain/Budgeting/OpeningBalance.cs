namespace FinanceApp.Domain.Budgeting;

/// "How much I have right now, for everything until the end of the month" — the answer to
/// installing the app mid-month. Without it the app spreads a WHOLE month's budget over the
/// days that are left and promises money that was already spent before install.
///
/// Chosen over pro-rating the budget or asking for a lump "already spent" figure because it
/// is the only one of the three that needs no memory: the number is on the banking app's
/// front screen. Nothing has to be entered retroactively.
///
/// Only a row dated inside the CURRENT month counts. Next month the ordinary budget takes
/// over on its own — this expires instead of needing to be cleared.
public class OpeningBalance : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    /// The day the balance was counted. Spending is measured from this day, inclusive.
    public DateOnly Date { get; set; }

    public decimal AmountOriginal { get; set; }
    public string CurrencyOriginal { get; set; } = Money.BaseCurrency;
    public decimal AmountBase { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
