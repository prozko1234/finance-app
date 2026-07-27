namespace FinanceApp.Domain.Savings;

using FinanceApp.Domain;

public enum SavingsEntryKind { Deposit, Withdrawal }

/// One real movement of the savings pot. The balance is the sum of these — never a
/// stored figure, so it can always be reconstructed and audited.
///
/// Money is stored exactly as on a transaction: what the user typed, plus the base amount
/// and the rate it was converted at. Someone saving in USD wants to see the USD they put
/// in, while the balance and the monthly goal can only work in one currency.
public class SavingsEntry
{
    public int Id { get; set; }
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

    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
