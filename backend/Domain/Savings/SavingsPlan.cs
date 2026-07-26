namespace FinanceApp.Domain.Savings;

public enum SavingsMode { Fixed, Percent }

/// How much Bohdan wants to put aside each month, on top of taxes.
/// MVP — a single active row. Percent is of the month's take-home (post-tax), because
/// that is the only money that is actually his to allocate.
public class SavingsPlan
{
    public int Id { get; set; }
    public SavingsMode Mode { get; set; } = SavingsMode.Fixed;
    public decimal Value { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}
