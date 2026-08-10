using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Common;
using FinanceApp.Application.Debts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using Microsoft.Extensions.Logging.Abstractions;
using static FinanceApp.Api.Tests.TestIncome;

namespace FinanceApp.Api.Tests;

/// «Скільки мені треба на місяць» — the half of the picture that a balance alone never gives.
/// An account that looks healthy against a month that costs more is the number people get
/// wrong, so the two have to be readable side by side.
public class MonthlyNeedTests
{
    [Fact]
    public async Task Standing_charges_are_put_on_a_monthly_scale_whatever_their_rhythm()
    {
        using var mem = new SqliteInMemory();
        var category = await CategoryAsync(mem);

        Add(mem, category, 60m, RecurrenceUnit.Month, 1);   // 60 a month
        Add(mem, category, 1_200m, RecurrenceUnit.Year, 1); // 100 a month
        Add(mem, category, 300m, RecurrenceUnit.Month, 3);  // 100 a month
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync();

        Assert.Equal(260m, Math.Round(r.Recurring, 2));
    }

    /// A weekly charge goes through the average year, not through "four weeks" — four would
    /// under-count it by a whole month's worth every year.
    [Fact]
    public async Task A_weekly_charge_counts_more_than_four_of_itself()
    {
        using var mem = new SqliteInMemory();
        Add(mem, await CategoryAsync(mem), 100m, RecurrenceUnit.Week, 1);
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync();

        Assert.True(r.Recurring > 400m);
        Assert.Equal(434.82m, Math.Round(r.Recurring, 2)); // 100 * 365.25 / 7 / 12
    }

    /// A paused subscription costs nothing while it is paused, and recurring income is not a
    /// thing the month asks for.
    [Fact]
    public async Task Paused_charges_and_recurring_income_are_not_asked_for()
    {
        using var mem = new SqliteInMemory();
        var category = await CategoryAsync(mem);

        Add(mem, category, 50m, RecurrenceUnit.Month, 1, active: false);
        Add(mem, category, 8_000m, RecurrenceUnit.Month, 1, kind: TransactionKind.Income);
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync();

        Assert.Equal(0m, r.Recurring);
    }

    /// The median of whole months, not of everything spent divided by three: the point is what
    /// a month usually costs, and one expensive month must not become the new usual.
    [Fact]
    public async Task Usual_spending_is_the_median_of_whole_months()
    {
        using var mem = new SqliteInMemory();
        var category = await CategoryAsync(mem);
        var firstOfThisMonth = FirstOfThisMonth();

        Spend(mem, category, 1_000m, firstOfThisMonth.AddMonths(-1).AddDays(3));
        Spend(mem, category, 4_000m, firstOfThisMonth.AddMonths(-2).AddDays(3));
        Spend(mem, category, 1_200m, firstOfThisMonth.AddMonths(-3).AddDays(3));
        // This month is still running — half a month is not a month.
        Spend(mem, category, 90m, firstOfThisMonth);
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync();

        Assert.True(r.TypicalKnown);
        Assert.Equal(1_200m, r.Typical);
    }

    /// A first-month user must not be handed a figure invented from a fortnight.
    [Fact]
    public async Task Usual_spending_says_nothing_until_there_is_history()
    {
        using var mem = new SqliteInMemory();
        var category = await CategoryAsync(mem);
        Spend(mem, category, 900m, FirstOfThisMonth().AddMonths(-1).AddDays(2));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync();

        Assert.False(r.TypicalKnown);
        Assert.Null(r.Typical);
    }

    /// Standing charges and money paid out of a jar have their own lines, so counting them in
    /// "usual" as well would ask for the same money twice.
    [Fact]
    public async Task Usual_spending_leaves_out_what_the_other_lines_already_cover()
    {
        using var mem = new SqliteInMemory();
        var category = await CategoryAsync(mem);
        var recurring = Add(mem, category, 50m, RecurrenceUnit.Month, 1);
        await mem.Db.SaveChangesAsync();

        var firstOfThisMonth = FirstOfThisMonth();
        foreach (var back in new[] { 1, 2 })
        {
            var day = firstOfThisMonth.AddMonths(-back).AddDays(3);
            Spend(mem, category, 500m, day);
            Spend(mem, category, 999m, day.AddDays(1), recurringId: recurring.Id);
        }
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync();

        Assert.Equal(500m, r.Typical);
    }

    [Fact]
    public async Task The_total_is_every_line_added_up()
    {
        using var mem = new SqliteInMemory();
        var category = await CategoryAsync(mem);
        Add(mem, category, 60m, RecurrenceUnit.Month, 1);

        var firstOfThisMonth = FirstOfThisMonth();
        Spend(mem, category, 800m, firstOfThisMonth.AddMonths(-1).AddDays(3));
        Spend(mem, category, 800m, firstOfThisMonth.AddMonths(-2).AddDays(3));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync();

        Assert.Equal(r.Recurring + r.Jars + r.Debts + r.Typical, r.Total);
        Assert.Equal(860m, r.Total);
    }

    private static DateOnly FirstOfThisMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return new DateOnly(today.Year, today.Month, 1);
    }

    private static async Task<Category> CategoryAsync(SqliteInMemory mem)
    {
        var category = new Category { Name = "Підписки" };
        mem.Db.Categories.Add(category);
        mem.Db.Transactions.Add(Income(5_000m));
        await mem.Db.SaveChangesAsync();
        return category;
    }

    private static RecurringExpense Add(
        SqliteInMemory mem, Category category, decimal amount, RecurrenceUnit unit, int interval,
        bool active = true, TransactionKind kind = TransactionKind.Expense)
    {
        var r = new RecurringExpense
        {
            Kind = kind,
            AmountOriginal = amount,
            CurrencyOriginal = "PLN",
            CategoryId = category.Id,
            // Far enough back that nothing here depends on today's position in the month, and
            // inside the two-year catch-up so a stray occurrence cannot be written.
            StartsOn = DateOnly.FromDateTime(DateTime.Now).AddMonths(-1),
            Unit = unit,
            Interval = interval,
            Active = active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.RecurringExpenses.Add(r);
        return r;
    }

    private static void Spend(
        SqliteInMemory mem, Category category, decimal amount, DateOnly on, int? recurringId = null)
    {
        mem.Db.Transactions.Add(new Transaction
        {
            Kind = TransactionKind.Expense,
            CurrencyOriginal = "PLN", AmountOriginal = amount, AmountBase = amount,
            FxRate = 1m, FxDate = on, Date = on, CategoryId = category.Id,
            RecurringExpenseId = recurringId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static MonthlyNeedService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        var periods = new BudgetPeriodResolver(mem.Db);
        var debts = new DebtLedger(mem.Db, periods);
        var budget = new MonthlyBudget(mem.Db, periods, debts);

        return new MonthlyNeedService(
            mem.Db, fx, periods, debts, budget,
            new EnvelopeService(
                mem.Db, new AllocationService(mem.Db), periods, fx, debts,
                NullLogger<EnvelopeService>.Instance),
            new MoneyViewFactory(mem.Db, fx));
    }
}
