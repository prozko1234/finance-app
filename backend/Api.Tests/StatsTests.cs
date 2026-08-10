using FinanceApp.Application.Common;
using FinanceApp.Application.Display;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Stats;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using FinanceApp.Api.Tests.Integration;

namespace FinanceApp.Api.Tests;

/// The statistics tab. The money question it must never get wrong: a finished month keeps
/// the size it had when it finished, whatever today's rate does.
public class StatsTests
{
    private static StatsService Sut(SqliteInMemory mem, IFxConverter? fx = null)
    {
        fx ??= new FakeFxConverter();
        return new StatsService(mem.Db, new MoneyViewFactory(mem.Db, fx), new RecurringMaterializer(mem.Db, fx, new BudgetPeriodResolver(mem.Db)));
    }

    private static async Task<int> CategoryAsync(SqliteInMemory mem, string name)
    {
        var c = new Category { Name = name };
        mem.Db.Categories.Add(c);
        await mem.Db.SaveChangesAsync();
        return c.Id;
    }

    private static Transaction Tx(decimal amountBase, DateOnly date, int categoryId, TransactionKind kind) =>
        new()
        {
            Kind = kind,
            CurrencyOriginal = "PLN", AmountOriginal = amountBase, AmountBase = amountBase,
            FxRate = 1m, FxDate = date, Date = date, CategoryId = categoryId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static SavingsEntry Entry(
        decimal amountBase, DateOnly date, int envelopeId, SavingsEntryKind kind, bool isAuto) =>
        new()
        {
            EnvelopeId = envelopeId, Date = date, Kind = kind,
            CurrencyOriginal = "PLN", AmountOriginal = amountBase, AmountBase = amountBase,
            FxRate = 1m, FxDate = date, IsAuto = isAuto, CreatedAt = DateTimeOffset.UtcNow,
        };

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public async Task Draws_one_column_per_month_with_income_against_expense()
    {
        using var mem = new SqliteInMemory();
        var cat = await CategoryAsync(mem, "Їжа");
        var lastMonth = Today.AddMonths(-1);

        mem.Db.Transactions.AddRange(
            Tx(8_000m, Today, cat, TransactionKind.Income),
            Tx(500m, Today, cat, TransactionKind.Expense),
            Tx(300m, Today, cat, TransactionKind.Expense),
            Tx(1_000m, lastMonth, cat, TransactionKind.Expense));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync(months: 3, month: null);

        Assert.Equal(3, r.Months.Count);
        Assert.Equal(Today.ToString("yyyy-MM"), r.Months[^1].Month);

        var current = r.Months[^1];
        Assert.Equal(8_000m, current.Income);
        Assert.Equal(800m, current.Expense);
        Assert.Equal(7_200m, current.Net);

        // A month with expenses and no income is a negative column, not a missing one.
        Assert.Equal(-1_000m, r.Months[^2].Net);
        // And a month with nothing in it still gets a column, or the chart would lie by omission.
        Assert.Equal(0m, r.Months[0].Expense);
    }

    /// «Скільки я відкладаю» is two numbers, not one: what the allocation scheme moves by
    /// itself, and what the user does on top of it. A month whose jars were raided must read
    /// as saving less, not as saving the plan.
    [Fact]
    public async Task Separates_what_the_scheme_put_aside_from_what_the_user_did()
    {
        using var mem = new SqliteInMemory();
        var cat = await CategoryAsync(mem, "Їжа");
        var jar = new Envelope { Name = "Подушка", Kind = BucketKind.Savings, CreatedAt = DateTimeOffset.UtcNow };
        mem.Db.Envelopes.Add(jar);
        await mem.Db.SaveChangesAsync();

        mem.Db.Transactions.Add(Tx(10_000m, Today, cat, TransactionKind.Income));
        mem.Db.SavingsEntries.AddRange(
            Entry(2_000m, Today, jar.Id, SavingsEntryKind.Deposit, isAuto: true),
            Entry(500m, Today, jar.Id, SavingsEntryKind.Deposit, isAuto: false),
            Entry(300m, Today, jar.Id, SavingsEntryKind.Withdrawal, isAuto: false));

        // Paid to a shop straight out of the jar: the jar shrank, and the scheme did not do it.
        var fromJar = Tx(200m, Today, cat, TransactionKind.Expense);
        fromJar.EnvelopeId = jar.Id;
        mem.Db.Transactions.Add(fromJar);
        await mem.Db.SaveChangesAsync();

        var current = (await Sut(mem).GetAsync(months: 2, month: null)).Months[^1];

        Assert.Equal(2_000m, current.SavedByPlan);
        Assert.Equal(0m, current.SavedByHand); // 500 in, 300 out, 200 spent from the jar
    }

    [Fact]
    public async Task Breaks_the_selected_month_down_by_category_biggest_first()
    {
        using var mem = new SqliteInMemory();
        var food = await CategoryAsync(mem, "Їжа");
        var fun = await CategoryAsync(mem, "Розваги");

        mem.Db.Transactions.AddRange(
            Tx(600m, Today, food, TransactionKind.Expense),
            Tx(150m, Today, food, TransactionKind.Expense),
            Tx(250m, Today, fun, TransactionKind.Expense),
            // Income never appears among the categories: the screen answers "на що пішли гроші".
            Tx(9_000m, Today, food, TransactionKind.Income));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync(months: 6, month: null);

        Assert.Equal(1_000m, r.SelectedExpense);
        Assert.Collection(r.Categories,
            c => { Assert.Equal("Їжа", c.Name); Assert.Equal(750m, c.Amount); Assert.Equal(75m, c.Percent); Assert.Equal(2, c.Count); },
            c => { Assert.Equal("Розваги", c.Name); Assert.Equal(250m, c.Amount); Assert.Equal(25m, c.Percent); });
    }

    [Fact]
    public async Task An_unparsable_month_falls_back_to_this_one_and_says_so()
    {
        using var mem = new SqliteInMemory();
        var cat = await CategoryAsync(mem, "Їжа");
        mem.Db.Transactions.Add(Tx(100m, Today, cat, TransactionKind.Expense));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync(months: 6, month: "не місяць");

        Assert.Equal(Today.ToString("yyyy-MM"), r.SelectedMonth);
        Assert.Equal(100m, r.SelectedExpense);
    }

    [Fact]
    public async Task A_past_month_is_converted_at_its_own_month_end_rate()
    {
        using var mem = new SqliteInMemory();
        mem.Db.AppSettings.Add(new AppSettings { DisplayCurrency = "USD" });
        var cat = await CategoryAsync(mem, "Їжа");

        var lastMonth = Today.AddMonths(-1);
        var lastMonthEnd = new DateOnly(lastMonth.Year, lastMonth.Month,
            DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month));

        // 5 PLN per USD when that month closed, 4 PLN per USD today. If the column took
        // today's rate, last month's 1000 PLN would have grown from 200 to 250 USD
        // overnight without a single transaction changing.
        var fx = new RateByDateFx(new() { [lastMonthEnd] = 5m }, fallback: 4m);

        mem.Db.Transactions.AddRange(
            Tx(1_000m, lastMonth, cat, TransactionKind.Expense),
            Tx(400m, Today, cat, TransactionKind.Expense));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem, fx).GetAsync(months: 2, month: null);

        Assert.Equal("USD", r.Currency);
        Assert.Equal(200m, r.Months[0].Expense);   // 1000 / 5, the rate that month closed at
        Assert.Equal(100m, r.Months[^1].Expense);  // 400 / 4, the running month at today's
    }

    /// The figure the whole "куди більше йде" card is built on. Median, not average: with an
    /// average, 100/200/900 would call 400 normal and this month's 400 unremarkable.
    [Fact]
    public async Task Calls_the_median_of_the_three_months_before_it_typical()
    {
        using var mem = new SqliteInMemory();
        var cat = await CategoryAsync(mem, "Їжа");

        mem.Db.Transactions.AddRange(
            Tx(100m, Today.AddMonths(-3), cat, TransactionKind.Expense),
            Tx(200m, Today.AddMonths(-2), cat, TransactionKind.Expense),
            Tx(900m, Today.AddMonths(-1), cat, TransactionKind.Expense),
            Tx(400m, Today, cat, TransactionKind.Expense));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync(months: 6, month: null);

        Assert.Equal(200m, r.Categories.Single().Typical);
    }

    /// A month with nothing in it is a month the app went unused, not a month that cost
    /// nothing. Counting it would tell someone coming back that everything has doubled.
    [Fact]
    public async Task Ignores_months_with_no_spending_at_all_when_working_out_the_normal()
    {
        using var mem = new SqliteInMemory();
        var cat = await CategoryAsync(mem, "Їжа");

        mem.Db.Transactions.AddRange(
            Tx(300m, Today.AddMonths(-3), cat, TransactionKind.Expense),
            // Nothing at all in the two months between.
            Tx(500m, Today, cat, TransactionKind.Expense));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync(months: 6, month: null);

        // One observed month is not a normal, so nothing is claimed rather than "+67%".
        Assert.Null(r.Categories.Single().Typical);
    }

    /// A category that was never bought before this month has no normal of its own, and
    /// "вилізло за межу" for something first bought yesterday is noise.
    [Fact]
    public async Task Says_nothing_about_a_category_that_is_new_this_month()
    {
        using var mem = new SqliteInMemory();
        var food = await CategoryAsync(mem, "Їжа");
        var newOne = await CategoryAsync(mem, "Курси");

        mem.Db.Transactions.AddRange(
            Tx(100m, Today.AddMonths(-2), food, TransactionKind.Expense),
            Tx(100m, Today.AddMonths(-1), food, TransactionKind.Expense),
            Tx(100m, Today, food, TransactionKind.Expense),
            Tx(800m, Today, newOne, TransactionKind.Expense));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetAsync(months: 6, month: null);

        Assert.Null(r.Categories.Single(c => c.CategoryId == newOne).Typical);
        Assert.Equal(100m, r.Categories.Single(c => c.CategoryId == food).Typical);
    }

    /// Rates that differ by date, so a test can prove WHICH date a column was drawn at.
    private sealed class RateByDateFx(Dictionary<DateOnly, decimal> rates, decimal fallback) : IFxConverter
    {
        public Task<Result<FxConversion>> ConvertToBaseAsync(
            decimal amount, string currency, DateOnly date, CancellationToken ct = default) =>
            Quote(amount * Rate(date), Rate(date), date);

        public Task<Result<FxConversion>> ConvertFromBaseAsync(
            decimal baseAmount, string currency, DateOnly date, CancellationToken ct = default) =>
            Quote(Math.Round(baseAmount / Rate(date), 2, MidpointRounding.AwayFromZero), Rate(date), date);

        private decimal Rate(DateOnly date) => rates.TryGetValue(date, out var r) ? r : fallback;

        private static Task<Result<FxConversion>> Quote(decimal amount, decimal rate, DateOnly date) =>
            Task.FromResult(Result<FxConversion>.Ok(new FxConversion(amount, rate, date)));
    }
}
