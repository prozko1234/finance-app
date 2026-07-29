using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
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
    IBudgetPeriods periods) : ISummaryService
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

        var spent = await monthRows
            .Where(t => t.Kind == TransactionKind.Expense)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var spentToday = await monthRows
            .Where(t => t.Kind == TransactionKind.Expense && t.Date == today)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var recurring = await ReservedRecurringAsync(today, period, ct);
        var allocation = await allocations.BreakdownAsync(budget ?? 0m, ct);

        // Everything the scheme does NOT hand to spending is an envelope, and every envelope
        // holds back the same way: what is still to reserve, plus what has already been moved
        // by hand. Deposits count too — a deposit is not an expense transaction, so without
        // them the money that left the account would come back as spendable, and the goal
        // would reserve less the more of it was actually saved.
        var envelopes = await envelopeService.StatusAsync(
            budget ?? 0m, month.FromOpeningBalance ? month.WindowStart : null, ct);
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
                await show(e.DepositedThisMonth), await show(e.StillToReserve)))),
            new AllocationSummary(
                allocation.SchemeName, allocation.Preset,
                await show(allocation.Spendable), await show(allocation.Reserved),
                await Task.WhenAll(allocation.Shares
                    .Select(async s => new BucketShareResponse(
                        s.BucketId, s.Name, s.Kind.ToString(), s.Percent, await show(s.Amount))))),
            month.WindowStart,
            month.FromOpeningBalance,
            period.Start,
            period.End);
    }

    /// Active recurring EXPENSES whose charge in THIS PERIOD is still in the future = not yet
    /// spent. Recurring income is excluded: it raises the budget when it lands, and
    /// reserving it here would subtract the salary from what the user may spend.
    /// Converted at today's rate (an estimate; the real rate is locked when it materializes).
    private async Task<decimal> ReservedRecurringAsync(
        DateOnly today, BudgetPeriod period, CancellationToken ct)
    {
        var recurring = await db.RecurringExpenses
            .Where(r => r.Active && r.Kind == TransactionKind.Expense)
            .ToListAsync(ct);
        var reserved = 0m;

        foreach (var r in recurring)
        {
            foreach (var occ in OccurrencesIn(period, r.DayOfMonth))
            {
                if (occ <= today) continue; // already charged (materialized into spent)

                var conv = await fx.ConvertToBaseAsync(r.AmountOriginal, r.CurrencyOriginal, today, ct);
                if (conv.IsSuccess) reserved += conv.Value!.AmountBase;
            }
        }

        return reserved;
    }

    /// When the period does not start on the 1st it straddles two calendar months, and a
    /// charge on the 5th belongs to whichever of them holds it. Looking only in today's
    /// month would miss the subscription that falls after the month turns but before the
    /// next payday — the app would promise money it has already committed.
    private static IEnumerable<DateOnly> OccurrencesIn(BudgetPeriod period, int dayOfMonth)
    {
        var first = RecurringSchedule.OccurrenceDate(period.Start.Year, period.Start.Month, dayOfMonth);
        if (period.Contains(first)) yield return first;

        // Same month at both ends means a calendar-month period: one candidate, not two.
        if (period.End.Year == period.Start.Year && period.End.Month == period.Start.Month) yield break;

        var second = RecurringSchedule.OccurrenceDate(period.End.Year, period.End.Month, dayOfMonth);
        if (period.Contains(second)) yield return second;
    }
}
