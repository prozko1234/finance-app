using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Savings;
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
    IAppDbContext db, IFxConverter fx, IRecurringMaterializer materializer,
    IMonthlyBudget monthlyBudget, ISavingsService savings,
    IAllocationService allocations) : ISummaryService
{
    public async Task<SafeToSpendResponse> GetSafeToSpendAsync(CancellationToken ct = default)
    {
        // Turn any due recurring charges into real transactions before we sum.
        await materializer.MaterializeDueAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var (first, last) = MonthRange.Of(today);

        var monthRows = db.Transactions.Where(t => t.Date >= first && t.Date <= last);

        var spent = await monthRows
            .Where(t => t.Kind == TransactionKind.Expense)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var spentToday = await monthRows
            .Where(t => t.Kind == TransactionKind.Expense && t.Date == today)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var (budget, taxes) = await monthlyBudget.ResolveAsync(ct);

        // Savings the user has committed to but not yet moved: hidden from safe-to-spend,
        // exactly like a not-yet-charged subscription.
        var savingsStatus = await savings.StatusAsync(budget ?? 0m, ct);
        var recurring = await ReservedRecurringAsync(today, ct);

        // The active scheme decides how much of the budget is spendable at all. Its Savings
        // buckets are NOT added here: they already come through savingsStatus, where deposits
        // made this month reduce what is still held back.
        var allocation = await allocations.BreakdownAsync(budget ?? 0m, ct);
        var allocated = allocation.Reserved - (allocation.SavingsGoal ?? 0m);

        // Deposits already made count as held back too, not only what is left to reserve.
        // A deposit is not an expense transaction, so without this the money that moved into
        // the envelope would come back as spendable — the goal would reserve less the more
        // of it was actually saved.
        var savingsHeld = savingsStatus.DepositedThisMonth + savingsStatus.StillToReserve;

        var r = SafeToSpendCalculator.Calculate(
            budget, spent, spentToday, recurring + savingsHeld + allocated, today);

        // Reported separately from the recurring reserve: the month summary shows them as
        // two different rows, and lumping them together would make the column unreadable.
        return new SafeToSpendResponse(
            today, Money.BaseCurrency, r.BudgetSet, r.MonthlyBudget, r.SpentThisMonth,
            recurring, r.RemainingThisMonth, r.DaysLeftInMonth,
            r.DailyNorm, r.SpentToday, r.LeftToday, r.TomorrowIfStop, r.TomorrowIfOnPlan,
            taxes?.ToMonthBreakdown(),
            new SavingsSummary(
                savingsStatus.Balance, savingsStatus.MonthGoal,
                savingsStatus.DepositedThisMonth, savingsStatus.StillToReserve),
            new AllocationSummary(
                allocation.SchemeName, allocation.Preset,
                allocation.Spendable, allocation.Reserved,
                allocation.Shares
                    .Select(s => new BucketShareResponse(
                        s.BucketId, s.Name, s.Kind.ToString(), s.Percent, s.Amount))
                    .ToList()));
    }

    /// Active recurring EXPENSES whose this-month charge is still in the future = not yet
    /// spent. Recurring income is excluded: it raises the budget when it lands, and
    /// reserving it here would subtract the salary from what the user may spend.
    /// Converted at today's rate (an estimate; the real rate is locked when it materializes).
    private async Task<decimal> ReservedRecurringAsync(DateOnly today, CancellationToken ct)
    {
        var recurring = await db.RecurringExpenses
            .Where(r => r.Active && r.Kind == TransactionKind.Expense)
            .ToListAsync(ct);
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
