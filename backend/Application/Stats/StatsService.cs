using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Stats;

public interface IStatsService
{
    /// <paramref name="months"/> is how many calendar months back to draw, including this
    /// one. <paramref name="month"/> ("yyyy-MM") is the one broken down by category;
    /// null means the current month.
    Task<StatsResponse> GetAsync(int months, string? month, CancellationToken ct = default);
}

/// The statistics tab: income against expense per month, and where one month's expenses
/// went. Deliberately the whole of it — no filters, no ranges, no chart library. The
/// question this screen answers is "чи я взагалі виходжу в плюс і на що йде решта", and
/// two views answer it.
///
/// Deliberately still CALENDAR months, even though the budget now runs from payday to
/// payday (<see cref="Domain.Budgeting.BudgetPeriod"/>). A bar labelled "липень" that
/// actually covers 25.06–24.07 is harder to read than one that covers July, and this screen
/// answers "чи я виходжу в плюс", not "скільки лишилось" — that question belongs to the home
/// screen, which does follow the period. Worth revisiting if the two ever have to agree.
///
/// Past months are converted at the rate of the LAST DAY OF THEIR MONTH, not today's and
/// not each transaction's own. Today's rate would redraw last year's bars every morning;
/// per-transaction rates would make a bar disagree with the category slices inside it,
/// since the slices would each carry a different rate. One rate per column keeps a column
/// internally consistent and keeps a finished month finished — the same trade-off the
/// month summary makes, applied per column instead of per screen.
public sealed class StatsService(
    IAppDbContext db, IMoneyViewFactory moneyViews, IRecurringMaterializer materializer)
    : IStatsService
{
    public const int MaxMonths = 24;

    public async Task<StatsResponse> GetAsync(int months, string? month, CancellationToken ct = default)
    {
        // Same as the summary: a subscription that is due today is part of this month's
        // spending, whether or not the home screen has been opened since.
        await materializer.MaterializeDueAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Now);
        months = Math.Clamp(months, 1, MaxMonths);

        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstMonth = thisMonth.AddMonths(-(months - 1));
        var selected = ParseMonth(month) ?? thisMonth;

        // The category breakdown may be asked for a month outside the drawn window (a
        // deep link, a narrower chart later), so the query covers both.
        var from = selected < firstMonth ? selected : firstMonth;
        var lastDay = LastDayOf(thisMonth);

        var rows = await db.Transactions
            .Where(t => t.Date >= from && t.Date <= lastDay)
            .Select(t => new Row(t.Date, t.Kind, t.AmountBase, t.CategoryId, t.Category!.Name, t.Category.Icon))
            .ToListAsync(ct);

        // What went into the jars, and by whose hand. Deposits the scheme made are told apart
        // from the user's own so the screen can answer the question behind "скільки я
        // відкладаю" — how much of it happens by itself and how much still takes a decision.
        var moved = await db.SavingsEntries
            .Where(x => x.Date >= from && x.Date <= lastDay)
            .Select(x => new Moved(
                x.Date,
                x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase,
                x.IsAuto))
            .ToListAsync(ct);

        // Money paid to a shop straight out of a jar leaves it the same way a withdrawal does,
        // and it is never the scheme's doing — so it lands on the by-hand side.
        var spentFromJars = await db.Transactions
            .Where(t => t.EnvelopeId != null && t.Kind == TransactionKind.Expense
                        && t.Date >= from && t.Date <= lastDay)
            .Select(t => new Moved(t.Date, -t.AmountBase, false))
            .ToListAsync(ct);

        var view = await moneyViews.CurrentAsync(ct);

        var byMonth = rows.ToLookup(r => new DateOnly(r.Date.Year, r.Date.Month, 1));
        var savedByMonth = moved.Concat(spentFromJars)
            .ToLookup(m => new DateOnly(m.Date.Year, m.Date.Month, 1));
        var monthly = new List<MonthStatsResponse>(months);

        for (var m = firstMonth; m <= thisMonth; m = m.AddMonths(1))
        {
            var rate = RateDateFor(m, today);
            var income = byMonth[m].Where(r => r.Kind == TransactionKind.Income).Sum(r => r.AmountBase);
            var expense = byMonth[m].Where(r => r.Kind == TransactionKind.Expense).Sum(r => r.AmountBase);

            var byPlan = savedByMonth[m].Where(x => x.IsAuto).Sum(x => x.Amount);
            var byHand = savedByMonth[m].Where(x => !x.IsAuto).Sum(x => x.Amount);

            monthly.Add(new MonthStatsResponse(
                Key(m),
                await view.FromBaseAsync(income, rate, ct),
                await view.FromBaseAsync(expense, rate, ct),
                await view.FromBaseAsync(income - expense, rate, ct),
                await view.FromBaseAsync(byPlan, rate, ct),
                await view.FromBaseAsync(byHand, rate, ct)));
        }

        var selectedRate = RateDateFor(selected, today);
        var expenses = byMonth[selected].Where(r => r.Kind == TransactionKind.Expense).ToList();
        var total = expenses.Sum(r => r.AmountBase);

        var categories = new List<CategoryStatsResponse>();
        foreach (var g in expenses.GroupBy(r => (r.CategoryId, r.CategoryName, r.CategoryIcon))
                                  .OrderByDescending(g => g.Sum(r => r.AmountBase)))
        {
            var amount = g.Sum(r => r.AmountBase);
            categories.Add(new CategoryStatsResponse(
                g.Key.CategoryId, g.Key.CategoryName, g.Key.CategoryIcon,
                await view.FromBaseAsync(amount, selectedRate, ct),
                // Share is computed on the stored amounts, so it is unaffected by rounding
                // in the display currency and the slices still add up to 100%.
                total == 0 ? 0m : Math.Round(amount / total * 100m, 1, MidpointRounding.AwayFromZero),
                g.Count()));
        }

        return new StatsResponse(
            view.Currency,
            monthly,
            Key(selected),
            await view.FromBaseAsync(total, selectedRate, ct),
            categories);
    }

    /// The rate a whole month is drawn at: its last day, or today for the month still
    /// running (no source quotes a date in the future, and the running month has no
    /// finished size yet anyway).
    private static DateOnly RateDateFor(DateOnly month, DateOnly today)
    {
        var last = LastDayOf(month);
        return last > today ? today : last;
    }

    private static DateOnly LastDayOf(DateOnly month) =>
        new(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));

    private static string Key(DateOnly month) => month.ToString("yyyy-MM");

    private static DateOnly? ParseMonth(string? month) =>
        DateOnly.TryParseExact(month, "yyyy-MM", out var parsed) ? parsed : null;

    private record Row(
        DateOnly Date, TransactionKind Kind, decimal AmountBase,
        int CategoryId, string CategoryName, string? CategoryIcon);

    /// One movement of money into or out of a jar. Negative means the jar shrank.
    private record Moved(DateOnly Date, decimal Amount, bool IsAuto);
}
