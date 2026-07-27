using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
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
    IAllocationService allocations, IMoneyViewFactory moneyViews) : ISummaryService
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
        // The whole summary is converted at ONE rate — today's — even the sum of past
        // spending. Per-date rates would be more faithful to each transaction, but then
        // budget − spent ≠ remaining on screen, and a user checking the arithmetic would
        // find the app wrong. Internal consistency beats per-row precision here; the
        // transaction list, where each row stands alone, does use per-date rates.
        var view = await moneyViews.CurrentAsync(ct);
        var show = (decimal v) => view.FromBaseTodayAsync(v, ct);

        return new SafeToSpendResponse(
            today, view.Currency, r.BudgetSet,
            r.MonthlyBudget is null ? null : await show(r.MonthlyBudget.Value),
            await show(r.SpentThisMonth),
            await show(recurring),
            r.RemainingThisMonth is null ? null : await show(r.RemainingThisMonth.Value),
            r.DaysLeftInMonth,
            r.DailyNorm is null ? null : await show(r.DailyNorm.Value),
            await show(r.SpentToday),
            r.LeftToday is null ? null : await show(r.LeftToday.Value),
            r.TomorrowIfStop is null ? null : await show(r.TomorrowIfStop.Value),
            r.TomorrowIfOnPlan is null ? null : await show(r.TomorrowIfOnPlan.Value),
            // Taxes stay in PLN: the engine is Polish and the split is what the accountant
            // will see. The UI says so out loud rather than converting it quietly.
            taxes?.ToMonthBreakdown(),
            new SavingsSummary(
                await show(savingsStatus.Balance), await show(savingsStatus.MonthGoal),
                await show(savingsStatus.DepositedThisMonth), await show(savingsStatus.StillToReserve)),
            new AllocationSummary(
                allocation.SchemeName, allocation.Preset,
                await show(allocation.Spendable), await show(allocation.Reserved),
                await Task.WhenAll(allocation.Shares
                    .Select(async s => new BucketShareResponse(
                        s.BucketId, s.Name, s.Kind.ToString(), s.Percent, await show(s.Amount))))));
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
