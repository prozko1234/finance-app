using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Fx;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Summaries;

public interface ISummaryService
{
    Task<SafeToSpendResponse> GetSafeToSpendAsync(CancellationToken ct = default);
}

public sealed class SummaryService(
    IAppDbContext db, IFxConverter fx, IRecurringMaterializer materializer) : ISummaryService
{
    public async Task<SafeToSpendResponse> GetSafeToSpendAsync(CancellationToken ct = default)
    {
        // Turn any due recurring charges into real transactions before we sum.
        await materializer.MaterializeDueAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var first = new DateOnly(today.Year, today.Month, 1);
        var last = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var monthRows = db.Transactions.Where(t => t.Date >= first && t.Date <= last);

        var spent = await monthRows
            .Where(t => t.Kind == TransactionKind.Expense)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        // Income rows store revenue (przychód, VAT excluded) in AmountBase.
        var revenue = await monthRows
            .Where(t => t.Kind == TransactionKind.Income)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var reserved = await ReservedRecurringAsync(today, ct);
        var budget = await ResolveBudgetAsync(revenue, ct);

        var r = SafeToSpendCalculator.Calculate(budget, spent, reserved, today);

        return new SafeToSpendResponse(
            today, Money.BaseCurrency, r.BudgetSet, r.MonthlyBudget, r.SpentThisMonth,
            r.ReservedRecurring, r.RemainingThisMonth, r.DaysLeftInMonth, r.SafeToSpendToday);
    }

    /// Budget comes from this month's income after tax. Taxes (ZUS, health, ryczalt) are
    /// MONTHLY in Poland, so they are applied once to the month's total revenue — never
    /// per invoice, which would double-count contributions on multi-invoice months.
    /// Falls back to the manually set budget when there is no income recorded yet.
    private async Task<decimal?> ResolveBudgetAsync(decimal monthlyRevenue, CancellationToken ct)
    {
        if (monthlyRevenue > 0)
        {
            var profile = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            if (profile is not null)
            {
                var take = TakeHomeCalculator.Calculate(profile, monthlyRevenue, amountIncludesVat: false);
                if (take.IsSuccess) return take.Value!.TakeHome;
            }
            return monthlyRevenue; // no usable tax profile — better than showing nothing
        }

        var manual = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return manual?.MonthlyAmount;
    }

    /// Active recurring whose this-month charge is still in the future = not yet spent.
    /// Converted at today's rate (an estimate; the real rate is locked when it materializes).
    private async Task<decimal> ReservedRecurringAsync(DateOnly today, CancellationToken ct)
    {
        var recurring = await db.RecurringExpenses.Where(r => r.Active).ToListAsync(ct);
        var reserved = 0m;

        foreach (var r in recurring)
        {
            var occ = RecurringSchedule.OccurrenceDate(today.Year, today.Month, r.DayOfMonth);
            if (occ <= today) continue; // already charged (materialized into spent)

            var conv = await fx.ConvertToBaseAsync(r.AmountOriginal, r.CurrencyOriginal, today, ct);
            if (conv.IsSuccess) reserved += conv.Value!.AmountBase;
        }

        return reserved;
    }
}
