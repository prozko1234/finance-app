namespace FinanceApp.Domain;

/// Місячний бюджет (allowance) у базовій валюті (PLN). У MVP — один активний запис.
public class Budget
{
    public int Id { get; set; }
    public decimal MonthlyAmount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
