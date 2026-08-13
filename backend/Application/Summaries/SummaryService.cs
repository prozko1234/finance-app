using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Debts;
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
    IBudgetPeriods periods, ICarryoverService carryover, IDebtLedger debts) : ISummaryService
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
        // Pending charges are left out: a recurring charge the schedule says fell due has not
        // been confirmed to have left the account, and it is still being held by the reserve
        // below. Counting it here as well would take the same money twice.
        var spent = await monthRows
            .Where(t => t.Kind == TransactionKind.Expense && t.EnvelopeId == null
                        && t.Status == TxStatus.Posted)
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

        // Paying somebody back is spending, and so is lending money out: either way the money
        // is gone, and a daily norm that did not feel it would be describing an account the
        // user no longer has. Only movements out of spendable money count — one taken from a
        // jar was already held back, and one marked as having happened before it was written
        // down never left this period at all.
        var debtsOut = await debts.OutOfSpendableAsync(month.WindowStart, last, ct);
        spent += debtsOut;
        spentToday += await debts.OutOfSpendableAsync(today, today, ct);

        var recurring = await ReservedRecurringAsync(today, period, ct);
        var debtsReserved = await debts.ReservedAsync(period, ct);
        var allocation = await allocations.BreakdownAsync(budget ?? 0m, ct);

        // Everything the scheme does NOT hand to spending is an envelope, and every envelope
        // holds back the same way: what is still to reserve, plus what has already been moved
        // by hand. Deposits count too — a deposit is not an expense transaction, so without
        // them the money that left the account would come back as spendable, and the goal
        // would reserve less the more of it was actually saved.
        var envelopes = await envelopeService.StatusAsync(month, ct);
        var heldBack = envelopes.Sum(e => e.HeldBack);

        var r = SafeToSpendCalculator.Calculate(
            budget, spent, spentToday, recurring + heldBack + debtsReserved, today, period);

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

        // Only charges whose day has already passed: one still ahead is not something the
        // user can answer yet, and a list of things to confirm later is a list to ignore.
        var pending = await db.Transactions
            .Where(t => t.Status == TxStatus.Pending && t.RecurringExpenseId != null
                        && t.Date >= period.Start && t.Date <= today)
            .OrderBy(t => t.Date)
            .Select(t => new
            {
                t.Id, t.AmountOriginal, t.CurrencyOriginal, t.AmountBase, t.Date, t.Note,
                CategoryName = t.Category!.Name,
            })
            .ToListAsync(ct);

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
                await show(left.Amount), left.FromStart, left.FromEnd, left.EnvelopeName),
            await show(debtsReserved),
            await Task.WhenAll(pending.Select(async p => new PendingChargeResponse(
                p.Id, string.IsNullOrWhiteSpace(p.Note) ? p.CategoryName : p.Note,
                p.AmountOriginal, p.CurrencyOriginal, await show(p.AmountBase), p.Date))),
            r.DaysThisWeek,
            r.LeftThisWeek is null ? null : await show(r.LeftThisWeek.Value));
    }

    /// Active recurring EXPENSES this period that have not been confirmed as paid. Recurring
    /// income is excluded: it raises the budget when it lands, and reserving it here would
    /// subtract the salary from what the user may spend.
    ///
    /// A charge stops being reserved when it is CONFIRMED, not when its day arrives. The day
    /// used to be the line, and it made the two halves of the same money disagree: the charge
    /// dropped out of the reserve and appeared in `spent` on the strength of the calendar
    /// alone, so the app insisted a subscription was paid while the account said otherwise.
    ///
    /// A charge already written (pending) is reserved at the amount it was written with,
    /// never re-converted. That is what makes confirming it a move between two columns rather
    /// than a change to the money: re-converting at today's rate would shift what is left by
    /// the difference between two days' rates every time a dollar subscription changed hands.
    /// Occurrences with no transaction yet have no such amount, so those still go at today's
    /// rate — an estimate, and the only honest one for a charge that has not happened.
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

        // The charges already written for this period, by the occurrence they belong to. One
        // row per (recurring, date) — the unique index guarantees it, so this cannot collide.
        var written = (await db.Transactions
                .Where(t => t.RecurringExpenseId != null
                            && t.Date >= period.Start && t.Date <= period.End)
                .Select(t => new { Id = t.RecurringExpenseId!.Value, t.Date, t.Status, t.AmountBase })
                .ToListAsync(ct))
            .ToDictionary(t => (t.Id, t.Date), t => (t.Status, t.AmountBase));

        // Every unconfirmed charge is held, whatever the rule that produced it says NOW. A row
        // written on the 15th and then moved to the 20th, or one whose subscription has since
        // been paused, is still a claim on real money — walking the current schedule alone
        // would free it silently, and editing a subscription would hand back money that is
        // still owed. Amounts come from the rows themselves, never re-converted: that is what
        // makes confirming a charge a move between columns rather than a change to the money.
        var reserved = written.Values
            .Where(c => c.Status == TxStatus.Pending)
            .Sum(c => c.AmountBase);

        foreach (var r in recurring)
        {
            foreach (var occ in RecurringSchedule.Occurrences(
                         r.StartsOn, r.Unit, r.Interval, period.Start, period.End))
            {
                if (skipped.Contains((r.Id, occ))) continue;
                // Posted is in `spent`; pending is already in the sum above.
                if (written.ContainsKey((r.Id, occ))) continue;

                // Nothing written yet, so there is no amount to take — today's rate is the
                // only honest estimate for a charge that has not happened.
                var conv = await fx.ConvertToBaseAsync(r.AmountOriginal, r.CurrencyOriginal, today, ct);
                if (conv.IsSuccess) reserved += conv.Value!.AmountBase;
            }
        }

        return reserved;
    }

}
