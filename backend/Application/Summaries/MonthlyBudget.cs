using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Domain;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Summaries;

public record MonthlyBudgetResult(decimal? Budget, TakeHomeBreakdown? Taxes);

public interface IMonthlyBudget
{
    Task<MonthlyBudgetResult> ResolveAsync(CancellationToken ct = default);
}

/// This month's budget: income after tax, or the manually set amount when there is no income.
/// Extracted so the summary and the savings goal (which can be a % of take-home) agree —
/// two places deriving take-home separately is how the numbers start to drift.
public sealed class MonthlyBudget(IAppDbContext db) : IMonthlyBudget
{
    public async Task<MonthlyBudgetResult> ResolveAsync(CancellationToken ct = default)
    {
        var (first, last) = MonthRange.Of(DateOnly.FromDateTime(DateTime.Now));

        // Income rows store revenue (przychód, VAT excluded) in AmountBase.
        var revenue = await db.Transactions
            .Where(t => t.Date >= first && t.Date <= last && t.Kind == TransactionKind.Income)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        if (revenue > 0)
        {
            var profile = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (profile is not null)
            {
                // Taxes are MONTHLY in Poland, so they are applied once to the month's total
                // revenue — never per invoice, which would double-count contributions.
                var take = TakeHomeCalculator.Calculate(profile, revenue, amountIncludesVat: false);
                if (take.IsSuccess) return new MonthlyBudgetResult(take.Value!.TakeHome, take.Value);
            }
            return new MonthlyBudgetResult(revenue, null); // no usable tax profile
        }

        var manual = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return new MonthlyBudgetResult(manual?.MonthlyAmount, null);
    }
}
