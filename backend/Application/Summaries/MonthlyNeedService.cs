using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Debts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Fx;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Summaries;

public interface IMonthlyNeedService
{
    Task<MonthlyNeedResponse> GetAsync(CancellationToken ct = default);
}

/// «Скільки мені треба грошей на місяць» — the other half of «скільки в мене зараз». One is
/// what is in the account, the other is what the month will ask for, and neither means much
/// without the other: a balance that looks healthy against a month that costs more is the
/// number people get wrong.
///
/// Four lines, in descending order of how little say the user has in them: the standing
/// charges, the plan's own deposits, what debts are holding back, and finally what ordinary
/// spending usually comes to. Only the last one is a guess, and it says so.
public sealed class MonthlyNeedService(
    IAppDbContext db, IFxConverter fx, IBudgetPeriods periods, IDebtLedger debts,
    IMonthlyBudget monthlyBudget, IEnvelopeService envelopes, IMoneyViewFactory moneyViews)
    : IMonthlyNeedService
{
    /// How far back "usually" looks. The same three months the statistics screen compares a
    /// category against, so the two screens cannot disagree about what usual means.
    private const int TypicalMonths = 3;

    /// Under two observed months there is no history worth the name, and a first-month user
    /// must not be handed a figure invented from a fortnight.
    private const int MinObservedMonths = 2;

    public async Task<MonthlyNeedResponse> GetAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var period = await periods.CurrentAsync(ct);

        var recurring = await RecurringPerMonthAsync(today, ct);
        var jars = (await envelopes.StatusAsync(await monthlyBudget.ResolveAsync(ct), ct))
            .Sum(e => e.MonthGoal);
        var debt = await debts.ReservedAsync(period, ct);
        var typical = await TypicalMonthAsync(today, ct);

        var view = await moneyViews.CurrentAsync(ct);
        var show = (decimal v) => view.FromBaseTodayAsync(v, ct);

        return new MonthlyNeedResponse(
            view.Currency,
            await show(recurring),
            await show(jars),
            await show(debt),
            typical is null ? null : await show(typical.Value),
            await show(recurring + jars + debt + (typical ?? 0m)),
            typical is not null);
    }

    /// Every active standing charge put on a monthly scale and converted at today's rate. The
    /// rate is an estimate by nature — this is a question about the months ahead, and there is
    /// no rate for those.
    private async Task<decimal> RecurringPerMonthAsync(DateOnly today, CancellationToken ct)
    {
        var rows = await db.RecurringExpenses
            .Where(r => r.Active && r.Kind == TransactionKind.Expense)
            .Select(r => new { r.AmountOriginal, r.CurrencyOriginal, r.Unit, r.Interval })
            .ToListAsync(ct);

        var total = 0m;
        foreach (var r in rows)
        {
            var perMonth = RecurringCost.PerMonth(r.AmountOriginal, r.Unit, r.Interval);
            var conv = await fx.ConvertToBaseAsync(perMonth, r.CurrencyOriginal, today, ct);
            if (conv.IsSuccess) total += conv.Value!.AmountBase;
        }

        return total;
    }

    /// What ordinary spending comes to in a month — the median of the last whole ones.
    ///
    /// Median rather than average, for the reason the statistics screen uses one: an average
    /// has last month's unusual month baked into it, and one flight makes the next four months
    /// look thrifty. Standing charges and money paid out of a jar are left out because they are
    /// already their own lines above; counting them here would ask for the same money twice.
    ///
    /// The month still running is not counted: half a month is not a month, and it would drag
    /// the figure down every time the screen was opened early in a period.
    private async Task<decimal?> TypicalMonthAsync(DateOnly today, CancellationToken ct)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var from = firstOfThisMonth.AddMonths(-TypicalMonths);

        var rows = await db.Transactions
            .Where(t => t.Kind == TransactionKind.Expense && t.EnvelopeId == null
                        && t.RecurringExpenseId == null
                        && t.Date >= from && t.Date < firstOfThisMonth)
            .Select(t => new { t.Date, t.AmountBase })
            .ToListAsync(ct);

        // A month with nothing in it is a month the app went unused, not a cheap month —
        // letting it in would tell a returning user that everything has doubled.
        var perMonth = rows
            .GroupBy(t => new DateOnly(t.Date.Year, t.Date.Month, 1))
            .Select(g => g.Sum(t => t.AmountBase))
            .Where(sum => sum > 0)
            .ToList();

        return perMonth.Count < MinObservedMonths ? null : Median(perMonth);
    }

    private static decimal Median(List<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[mid]
            : Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 2, MidpointRounding.AwayFromZero);
    }
}
