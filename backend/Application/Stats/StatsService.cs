using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
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
        var (_, lastDay) = MonthRange.Of(thisMonth);

        var rows = await db.Transactions
            .Where(t => t.Date >= from && t.Date <= lastDay)
            .Select(t => new Row(t.Date, t.Kind, t.AmountBase, t.CategoryId, t.Category!.Name, t.Category.Icon))
            .ToListAsync(ct);

        var view = await moneyViews.CurrentAsync(ct);

        var byMonth = rows.ToLookup(r => new DateOnly(r.Date.Year, r.Date.Month, 1));
        var monthly = new List<MonthStatsResponse>(months);

        for (var m = firstMonth; m <= thisMonth; m = m.AddMonths(1))
        {
            var rate = RateDateFor(m, today);
            var income = byMonth[m].Where(r => r.Kind == TransactionKind.Income).Sum(r => r.AmountBase);
            var expense = byMonth[m].Where(r => r.Kind == TransactionKind.Expense).Sum(r => r.AmountBase);

            monthly.Add(new MonthStatsResponse(
                Key(m),
                await view.FromBaseAsync(income, rate, ct),
                await view.FromBaseAsync(expense, rate, ct),
                await view.FromBaseAsync(income - expense, rate, ct)));
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
        var (_, last) = MonthRange.Of(month);
        return last > today ? today : last;
    }

    private static string Key(DateOnly month) => month.ToString("yyyy-MM");

    private static DateOnly? ParseMonth(string? month) =>
        DateOnly.TryParseExact(month, "yyyy-MM", out var parsed) ? parsed : null;

    private record Row(
        DateOnly Date, TransactionKind Kind, decimal AmountBase,
        int CategoryId, string CategoryName, string? CategoryIcon);
}
