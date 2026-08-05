using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Envelopes;
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
    IMonthlyBudget monthlyBudget, IEnvelopeService envelopeService,
    IAllocationService allocations, IMoneyViewFactory moneyViews,
    IBudgetPeriods periods, ICarryoverService carryover) : ISummaryService
{
    public async Task<SafeToSpendResponse> GetSafeToSpendAsync(CancellationToken ct = default)
    {
        // Turn any due recurring charges into real transactions before we sum.
        await materializer.MaterializeDueAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var period = await periods.CurrentAsync(ct);
        var last = period.End;

        var month = await monthlyBudget.ResolveAsync(ct);
        var (budget, taxes) = (month.Budget, month.Taxes);

        // Spending is counted from the window start, not from the day the period began. When
        // the user started mid-period by counting what they had, the days before that are
        // already inside that figure — summing them again would charge those expenses twice.
        var monthRows = db.Transactions.Where(t => t.Date >= month.WindowStart && t.Date <= last);

        // Expenses paid out of an envelope are left out on purpose. That money was taken out
        // of what is spendable when it went into the envelope; counting it again as it
        // leaves would charge the daily norm twice for one purchase.
        var spent = await monthRows
            .Where(t => t.Kind == TransactionKind.Expense && t.EnvelopeId == null)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        // A recurring charge that fell due today is NOT today's spending. Its money was set
        // aside at the start of the period and has been missing from the daily norm ever
        // since; counting it again on the day it lands takes the same money twice and hands
        // the user a minus for a decision they never made. It stays in `spent` above — the
        // period figure is right either way, because the reserve drops it the moment it
        // materializes — but the norm is about choices, and this was not one.
        var spentToday = await monthRows
            .Where(t => t.Kind == TransactionKind.Expense && t.EnvelopeId == null && t.Date == today
                        && t.RecurringExpenseId == null)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var recurring = await ReservedRecurringAsync(today, period, ct);
        var allocation = await allocations.BreakdownAsync(budget ?? 0m, ct);

        // Everything the scheme does NOT hand to spending is an envelope, and every envelope
        // holds back the same way: what is still to reserve, plus what has already been moved
        // by hand. Deposits count too — a deposit is not an expense transaction, so without
        // them the money that left the account would come back as spendable, and the goal
        // would reserve less the more of it was actually saved.
        var envelopes = await envelopeService.StatusAsync(month, ct);
        var heldBack = envelopes.Sum(e => e.HeldBack);

        var r = SafeToSpendCalculator.Calculate(
            budget, spent, spentToday, recurring + heldBack, today, period);

        // Reported separately from the recurring reserve: the month summary shows them as
        // two different rows, and lumping them together would make the column unreadable.
        // The whole summary is converted at ONE rate — today's — even the sum of past
        // spending. Per-date rates would be more faithful to each transaction, but then
        // budget − spent ≠ remaining on screen, and a user checking the arithmetic would
        // find the app wrong. Internal consistency beats per-row precision here; the
        // transaction list, where each row stands alone, does use per-date rates.
        var view = await moneyViews.CurrentAsync(ct);
        var show = (decimal v) => view.FromBaseTodayAsync(v, ct);

        var left = await carryover.PendingAsync(ct);

        return new SafeToSpendResponse(
            today, view.Currency, r.BudgetSet,
            r.PeriodBudget is null ? null : await show(r.PeriodBudget.Value),
            await show(r.SpentThisPeriod),
            await show(recurring),
            r.RemainingThisPeriod is null ? null : await show(r.RemainingThisPeriod.Value),
            r.DaysLeftInPeriod,
            r.DailyNorm is null ? null : await show(r.DailyNorm.Value),
            await show(r.SpentToday),
            r.LeftToday is null ? null : await show(r.LeftToday.Value),
            r.TomorrowIfStop is null ? null : await show(r.TomorrowIfStop.Value),
            r.TomorrowIfOnPlan is null ? null : await show(r.TomorrowIfOnPlan.Value),
            // Taxes stay in PLN: the engine is Polish and the split is what the accountant
            // will see. The UI says so out loud rather than converting it quietly.
            taxes?.ToMonthBreakdown(),
            await Task.WhenAll(envelopes.Select(async e => new EnvelopeSummary(
                e.Id, e.Name, e.Kind.ToString(), e.IsDefault,
                await show(e.Balance), await show(e.MonthGoal),
                await show(e.DepositedThisMonth), await show(e.StillToReserve), e.IsFromScheme))),
            new AllocationSummary(
                allocation.SchemeName, allocation.Preset,
                await show(allocation.Spendable), await show(allocation.Reserved),
                await Task.WhenAll(allocation.Shares
                    .Select(async s => new BucketShareResponse(
                        s.BucketId, s.Name, s.Kind.ToString(), s.Percent, await show(s.Amount))))),
            month.WindowStart,
            month.FromOpeningBalance,
            period.Start,
            period.End,
            // The leftover is converted like everything else on this screen: it is money the
            // user is about to make a decision about, and a figure in a currency they are not
            // reading in is not one they can decide with.
            left is null ? null : new CarryoverResponse(
                await show(left.Amount), left.FromStart, left.FromEnd, left.EnvelopeName));
    }

    /// Active recurring EXPENSES whose charge in THIS PERIOD is still in the future = not yet
    /// spent. Recurring income is excluded: it raises the budget when it lands, and
    /// reserving it here would subtract the salary from what the user may spend.
    /// Converted at today's rate (an estimate; the real rate is locked when it materializes).
    ///
    /// Occurrences the user has deleted are left out, exactly as
    /// <see cref="Recurring.RecurringMaterializer"/> leaves them out. The two have to agree:
    /// the reserve is a promise that a transaction is coming, and for a skipped date no
    /// transaction ever comes. Holding the money anyway lowers the daily norm for the rest of
    /// the period with nothing on screen to explain where it went.
    private async Task<decimal> ReservedRecurringAsync(
        DateOnly today, BudgetPeriod period, CancellationToken ct)
    {
        var recurring = await db.RecurringExpenses
            .Where(r => r.Active && r.Kind == TransactionKind.Expense)
            .ToListAsync(ct);

        // Loaded once for the whole period: the set is tiny, and asking per date would be a
        // query per charge per load.
        var skipped = (await db.RecurringSkips
                .Where(s => s.Date >= period.Start && s.Date <= period.End)
                .Select(s => new { s.RecurringExpenseId, s.Date })
                .ToListAsync(ct))
            .Select(s => (s.RecurringExpenseId, s.Date))
            .ToHashSet();

        var reserved = 0m;

        foreach (var r in recurring)
        {
            foreach (var occ in RecurringSchedule.Occurrences(
                         r.StartsOn, r.Unit, r.Interval, period.Start, period.End))
            {
                if (occ <= today) continue; // already charged (materialized into spent)
                if (skipped.Contains((r.Id, occ))) continue;

                var conv = await fx.ConvertToBaseAsync(r.AmountOriginal, r.CurrencyOriginal, today, ct);
                if (conv.IsSuccess) reserved += conv.Value!.AmountBase;
            }
        }

        return reserved;
    }

}
