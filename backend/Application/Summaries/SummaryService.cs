using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Fx;
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

        var spent = await db.Transactions
            .Where(t => t.Date >= first && t.Date <= last)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var reserved = await ReservedRecurringAsync(today, ct);

        var budget = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var r = SafeToSpendCalculator.Calculate(budget?.MonthlyAmount, spent, reserved, today);

        return new SafeToSpendResponse(
            today, Money.BaseCurrency, r.BudgetSet, r.MonthlyBudget, r.SpentThisMonth,
            r.ReservedRecurring, r.RemainingThisMonth, r.DaysLeftInMonth, r.SafeToSpendToday);
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
