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
/// charges, what debts are holding back, what ordinary spending usually comes to, and finally
/// the plan's own deposits. Only the third is a guess, and it says so.
///
/// The saving is the odd one out and is kept OUT of the headline total. Everything else here
/// is a bill; a plan to put 20% away is not, and folding it in made "треба на місяць" read
/// thousands above what the month really costs — which is precisely the figure someone
/// compares their balance against before deciding they are in trouble. It gets its own line
/// and its own combined total underneath, where it can be argued with.
public sealed class MonthlyNeedService(
    IAppDbContext db, IFxConverter fx, IBudgetPeriods periods, IDebtLedger debts,
    IMonthlyBudget monthlyBudget, IEnvelopeService envelopes, IMoneyViewFactory moneyViews)
    : IMonthlyNeedService
{
    /// How far back "usually" looks.
    ///
    /// Six, where the statistics screen's per-category comparison uses three. They are asking
    /// different questions: a category is compared against its recent self, and three months is
    /// enough for "продукти цього місяця дорожчі, ніж зазвичай". This figure is a whole month's
    /// living cost, read once and then compared against a bank balance — a median of three
    /// values is just the middle month, so one holiday or one dentist in three months moves it
    /// by hundreds and it never settles down.
    private const int LookBackMonths = 6;

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
        var months = await TypicalMonthsAsync(today, ct);
        decimal? typical = months.Count < MinObservedMonths
            ? null
            : Median(months.Select(m => m.Amount));

        var view = await moneyViews.CurrentAsync(ct);
        var show = (decimal v) => view.FromBaseTodayAsync(v, ct);

        var must = recurring + debt + (typical ?? 0m);

        return new MonthlyNeedResponse(
            view.Currency,
            await show(recurring),
            await show(jars),
            await show(debt),
            typical is null ? null : await show(typical.Value),
            await show(must),
            typical is not null,
            await show(must + jars),
            // Newest first: the months nearest to now are the ones worth arguing with.
            await Task.WhenAll(months
                .OrderByDescending(m => m.Month)
                .Select(async m => new TypicalMonthResponse(m.Month, await show(m.Amount)))));
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
    private async Task<List<(DateOnly Month, decimal Amount)>> TypicalMonthsAsync(
        DateOnly today, CancellationToken ct)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var from = firstOfThisMonth.AddMonths(-LookBackMonths);

        var rows = await db.Transactions
            // Posted only, like every other figure built on spending. Today every pending row
            // also carries a recurring id and would be dropped by the line below anyway — but
            // "usual spending" must not depend on that staying true.
            .Where(t => t.Kind == TransactionKind.Expense && t.EnvelopeId == null
                        && t.RecurringExpenseId == null && t.Status == TxStatus.Posted
                        && t.Date >= from && t.Date < firstOfThisMonth)
            .Select(t => new { t.Date, t.AmountBase })
            .ToListAsync(ct);

        // A month with nothing in it is a month the app went unused, not a cheap month —
        // letting it in would tell a returning user that everything has doubled.
        return rows
            .GroupBy(t => new DateOnly(t.Date.Year, t.Date.Month, 1))
            .Select(g => (Month: g.Key, Amount: g.Sum(t => t.AmountBase)))
            .Where(m => m.Amount > 0)
            .OrderBy(m => m.Month)
            .ToList();
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[mid]
            : Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 2, MidpointRounding.AwayFromZero);
    }
}
