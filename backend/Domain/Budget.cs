namespace FinanceApp.Domain;

/// Monthly budget (allowance) in base currency (PLN). MVP — a single active row.
public class Budget
{
    public int Id { get; set; }
    public decimal MonthlyAmount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
