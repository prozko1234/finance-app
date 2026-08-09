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

    /// How many months back a category's "typical" is taken from. Three is the smallest number
    /// a median can throw out an outlier from — with two, one holiday month IS the normal.
    public const int TypicalMonths = 3;

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
        // deep link, a narrower chart later), so the query covers both — and it reaches
        // TypicalMonths further back still, because a category's own normal is worked out
        // from the months before whichever one is being broken down.
        var earliest = selected.AddMonths(-TypicalMonths);
        var from = earliest < firstMonth ? earliest : firstMonth;
        var lastDay = LastDayOf(thisMonth);

        var rows = await db.Transactions
            .Where(t => t.Date >= from && t.Date <= lastDay)
            .Select(t => new Row(t.Date, t.Kind, t.AmountBase, t.CategoryId, t.Category!.Name, t.Category.Icon))
            .ToListAsync(ct);

        // What went into the jars, and by whose hand. Deposits the scheme made are told apart
        // from the user's own so the screen can answer the question behind "скільки я
        // відкладаю" — how much of it happens by itself and how much still takes a decision.
        // Money recorded as already put away is left out for the same reason the budget
        // ignores it: it was saved before, and counting it here would show a month where a
        // year's pot was typed in as a month of extraordinary saving.
        var moved = await db.SavingsEntries
            .Where(x => x.Date >= from && x.Date <= lastDay && !x.AlreadySetAside)
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

        var typical = TypicalByCategory(byMonth, selected, thisMonth);

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
                g.Count(),
                // Converted at the SELECTED month's rate, not each source month's own: the
                // number exists to be subtracted from Amount, and two rates would turn a
                // steady category into a phantom overrun every time the zloty moved.
                typical.TryGetValue(g.Key.CategoryId, out var usual)
                    ? await view.FromBaseAsync(usual, selectedRate, ct)
                    : null));
        }

        // Stock, not flow: everything the jars hold today, whenever it went in and whether or
        // not the app was there to see it. The monthly figures above deliberately leave out
        // money recorded as already set aside, so without this the screen would add up to less
        // than the jars actually hold and read as though something had gone missing.
        var savedBalance = await db.SavingsEntries
            .SumAsync(x => (decimal?)(x.Kind == SavingsEntryKind.Deposit
                ? x.AmountBase
                : -x.AmountBase), ct) ?? 0m;
        savedBalance -= await db.Transactions
            .Where(t => t.EnvelopeId != null && t.Kind == TransactionKind.Expense)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        return new StatsResponse(
            view.Currency,
            monthly,
            Key(selected),
            await view.FromBaseAsync(total, selectedRate, ct),
            categories,
            await view.FromBaseAsync(savedBalance, today, ct),
            await SavedByCurrencyAsync(ct));
    }

    /// What went into the jars, kept in the currency it was put in.
    ///
    /// The rest of this screen converts everything to one currency at one rate, which is right
    /// for comparing months. It is wrong for answering "скільки я відклав" when the answer is
    /// partly in złoty and partly in dollars: a single converted figure hides both what is
    /// actually held and the fact that half of it moves with the rate — which is the whole
    /// reason this app exists for people living between currencies.
    ///
    /// Returns nothing when it was all one currency; the total already said it, and a
    /// one-line breakdown of one thing is noise.
    private async Task<IReadOnlyList<CurrencyAmountResponse>?> SavedByCurrencyAsync(
        CancellationToken ct)
    {
        var byCurrency = await db.SavingsEntries
            .GroupBy(x => x.CurrencyOriginal)
            .Select(g => new CurrencyAmountResponse(
                g.Key,
                g.Sum(x => x.Kind == SavingsEntryKind.Deposit
                    ? x.AmountOriginal
                    : -x.AmountOriginal)))
            .ToListAsync(ct);

        var real = byCurrency.Where(x => x.Amount != 0m).OrderByDescending(x => x.Amount).ToList();
        return real.Count > 1 ? real : null;
    }

    /// What each category usually costs in a month, in base currency: the median of its totals
    /// over the <see cref="TypicalMonths"/> calendar months before the selected one.
    ///
    /// Median rather than average, because the whole point is to notice an unusual month, and an
    /// average has last month's unusual month baked into it — one 2 000 zł flight makes the next
    /// four months of travel look thrifty.
    ///
    /// A month with no expenses at all is not counted as a cheap month: it is a month the app
    /// went unused, and letting it in would tell a returning user that everything has doubled.
    /// Under two such months there is no history worth the name and nothing is returned, so a
    /// new user is never told their first month is an overrun. The month still running is left
    /// out for the same reason — half a month is not a month.
    ///
    /// A category with a typical of zero is dropped: it is new this month, and "+100% проти
    /// звичайного" for a category first used yesterday is noise, not a finding.
    private static Dictionary<int, decimal> TypicalByCategory(
        ILookup<DateOnly, Row> byMonth, DateOnly selected, DateOnly thisMonth)
    {
        var history = Enumerable.Range(1, TypicalMonths)
            .Select(back => selected.AddMonths(-back))
            .Where(m => m < thisMonth)
            .Select(m => byMonth[m].Where(r => r.Kind == TransactionKind.Expense).ToList())
            .Where(rows => rows.Count > 0)
            .ToList();

        if (history.Count < 2) return [];

        var perMonth = history
            .Select(rows => rows.GroupBy(r => r.CategoryId)
                                .ToDictionary(g => g.Key, g => g.Sum(r => r.AmountBase)))
            .ToList();

        var typical = new Dictionary<int, decimal>();
        foreach (var categoryId in perMonth.SelectMany(m => m.Keys).Distinct())
        {
            // A category missing from an observed month really was zero that month, and that
            // zero has to weigh on the median — otherwise a category bought once a quarter
            // reads as a monthly habit.
            var median = Median(perMonth.Select(m => m.GetValueOrDefault(categoryId)));
            if (median > 0) typical[categoryId] = median;
        }

        return typical;
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
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
