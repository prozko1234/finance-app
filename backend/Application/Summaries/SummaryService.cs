using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Summaries;

public interface ISummaryService
{
    Task<SafeToSpendResponse> GetSafeToSpendAsync(CancellationToken ct = default);
}

public sealed class SummaryService(IAppDbContext db) : ISummaryService
{
    public async Task<SafeToSpendResponse> GetSafeToSpendAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var first = new DateOnly(today.Year, today.Month, 1);
        var last = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var spent = await db.Transactions
            .Where(t => t.Date >= first && t.Date <= last)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var budget = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var r = SafeToSpendCalculator.Calculate(budget?.MonthlyAmount, spent, today);

        return new SafeToSpendResponse(
            today, Money.BaseCurrency, r.BudgetSet, r.MonthlyBudget,
            r.SpentThisMonth, r.RemainingThisMonth, r.DaysLeftInMonth, r.SafeToSpendToday);
    }
}
