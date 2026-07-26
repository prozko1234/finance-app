namespace FinanceApp.Domain.Savings;

public enum SavingsEntryKind { Deposit, Withdrawal }

/// One real movement of the savings pot. The balance is the sum of these — never a
/// stored figure, so it can always be reconstructed and audited.
public class SavingsEntry
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public SavingsEntryKind Kind { get; set; }
    public decimal Amount { get; set; }   // always positive; Kind carries the direction
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
