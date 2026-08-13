using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Common;
using FinanceApp.Application.Display;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Stats;
using FinanceApp.Domain;

namespace FinanceApp.Api.Tests;

/// «Куди більше йде» over the window a person actually lives in. The home screen already ranks
/// recent categories by how OFTEN they are used, which answers a different question — what gets
/// tapped a lot is rarely what costs a lot.
public class RecentSpendingTests
{
    [Fact]
    public async Task Categories_come_back_by_money_spent_not_by_how_often()
    {
        using var mem = new SqliteInMemory();
        var (food, taxi) = await TwoCategoriesAsync(mem);
        var today = DateOnly.FromDateTime(DateTime.Now);

        // Six coffees and one taxi, all today so the assertion does not depend on which day of
        // the week the suite runs on. The taxi costs more.
        for (var i = 0; i < 6; i++) Spend(mem, food, 15m, today);
        Spend(mem, taxi, 200m, today);
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetRecentAsync(StatsService.WeekWindow);

        Assert.Equal(200m, r.Categories[0].Amount);
        Assert.Equal(1, r.Categories[0].Count);
        Assert.Equal(90m, r.Categories[1].Amount);
        Assert.Equal(6, r.Categories[1].Count);
        Assert.Equal(290m, r.Total);
    }

    /// A weekly figure means nothing on its own: "їжа 380" is only readable next to "минулого
    /// тижня 240". Same weekday, one week back — so the two stretches are the same length.
    [Fact]
    public async Task The_same_stretch_one_week_back_comes_with_it()
    {
        using var mem = new SqliteInMemory();
        var (food, _) = await TwoCategoriesAsync(mem);
        var today = DateOnly.FromDateTime(DateTime.Now);

        Spend(mem, food, 380m, today);
        Spend(mem, food, 240m, today.AddDays(-7));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetRecentAsync(StatsService.WeekWindow);

        Assert.Equal(380m, r.Total);
        Assert.Equal(240m, r.PreviousTotal);
        Assert.Equal(240m, Assert.Single(r.Categories).PreviousAmount);
    }

    /// The comparison stops at the matching day. Counting the rest of last week against four
    /// days of this one is a fall in spending that never happened — and it is the reason a
    /// rolling window was replaced by a calendar one in the first place.
    [Fact]
    public async Task Last_week_is_cut_at_the_same_day_of_the_week()
    {
        using var mem = new SqliteInMemory();
        var (food, _) = await TwoCategoriesAsync(mem);
        var today = DateOnly.FromDateTime(DateTime.Now);

        Spend(mem, food, 100m, today.AddDays(-7)); // the matching day — counts
        Spend(mem, food, 900m, today.AddDays(-6)); // one day past it — does not
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetRecentAsync(StatsService.WeekWindow);

        Assert.Equal(100m, r.PreviousTotal);
    }

    /// The week starts on Monday. It used to be "the last seven days", which re-bases itself
    /// every morning — so no two readings are of the same thing, and "минулого тижня" meant
    /// something nobody else in the world means by it.
    [Fact]
    public async Task The_week_is_counted_from_Monday()
    {
        using var mem = new SqliteInMemory();
        await TwoCategoriesAsync(mem);

        var r = await Sut(mem).GetRecentAsync(StatsService.WeekWindow);

        Assert.Equal(DayOfWeek.Monday, r.From.DayOfWeek);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), r.To);
    }

    [Fact]
    public async Task The_month_is_counted_from_the_first()
    {
        using var mem = new SqliteInMemory();
        await TwoCategoriesAsync(mem);

        var r = await Sut(mem).GetRecentAsync(StatsService.MonthWindow);

        Assert.Equal(1, r.From.Day);
        Assert.Equal(1, r.PreviousFrom.Day);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now).AddMonths(-1).Month, r.PreviousFrom.Month);
    }

    /// Money paid out of a jar stopped being spendable when it went in, so counting it would
    /// make a quiet week look like a blow-out on the day a saved-up purchase happens. An
    /// unconfirmed subscription charge is out for the same reason it is out of the daily norm:
    /// nobody has said that money has gone.
    [Fact]
    public async Task Jar_spending_and_unconfirmed_charges_are_left_out()
    {
        using var mem = new SqliteInMemory();
        var (food, _) = await TwoCategoriesAsync(mem);
        var today = DateOnly.FromDateTime(DateTime.Now);

        var jar = new Domain.Savings.Envelope { Name = "Відпустка" };
        mem.Db.Envelopes.Add(jar);
        await mem.Db.SaveChangesAsync();

        Spend(mem, food, 100m, today);
        Spend(mem, food, 900m, today, envelopeId: jar.Id);
        Spend(mem, food, 50m, today, status: TxStatus.Pending);
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetRecentAsync(StatsService.WeekWindow);

        Assert.Equal(100m, r.Total);
    }

    /// Only the two windows worth asking about. Anything else falls back to the week rather
    /// than answering a question nobody asked.
    [Fact]
    public async Task An_unsupported_window_falls_back_to_the_week()
    {
        using var mem = new SqliteInMemory();
        await TwoCategoriesAsync(mem);

        Assert.Equal(StatsService.WeekWindow, (await Sut(mem).GetRecentAsync("year")).Window);
        Assert.Equal(StatsService.WeekWindow, (await Sut(mem).GetRecentAsync(null)).Window);
        Assert.Equal(StatsService.MonthWindow, (await Sut(mem).GetRecentAsync("month")).Window);
    }

    [Fact]
    public async Task A_week_with_nothing_in_it_is_not_an_error()
    {
        using var mem = new SqliteInMemory();
        await TwoCategoriesAsync(mem);

        var r = await Sut(mem).GetRecentAsync(StatsService.WeekWindow);

        Assert.Empty(r.Categories);
        Assert.Equal(0m, r.Total);
    }

    private static async Task<(Category Food, Category Taxi)> TwoCategoriesAsync(SqliteInMemory mem)
    {
        var food = new Category { Name = "Їжа", Icon = "🍎" };
        var taxi = new Category { Name = "Таксі", Icon = "🚕" };
        mem.Db.Categories.AddRange(food, taxi);
        await mem.Db.SaveChangesAsync();
        return (food, taxi);
    }

    private static void Spend(
        SqliteInMemory mem, Category category, decimal amount, DateOnly on,
        int? envelopeId = null, TxStatus status = TxStatus.Posted)
    {
        mem.Db.Transactions.Add(new Transaction
        {
            Kind = TransactionKind.Expense,
            CurrencyOriginal = "PLN", AmountOriginal = amount, AmountBase = amount,
            FxRate = 1m, FxDate = on, Date = on, CategoryId = category.Id,
            EnvelopeId = envelopeId, Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static StatsService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new StatsService(
            mem.Db, new MoneyViewFactory(mem.Db, fx),
            new RecurringMaterializer(mem.Db, fx, new BudgetPeriodResolver(mem.Db)));
    }
}
