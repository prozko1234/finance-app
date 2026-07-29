using FinanceApp.Application.Common;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests;

/// Before envelopes, a scheme's Пенсія bucket only ever subtracted from the daily norm: the
/// app held the money back every month and could never say how much had piled up, or let
/// the user actually put it anywhere. These tests are about that gap being closed.
public class EnvelopeTests
{
    private const decimal Budget = 6_000m;

    private static EnvelopeService Sut(SqliteInMemory mem) =>
        new(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db));

    private static SavingsService Savings(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SavingsService(
            mem.Db, new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db)), fx, new AllocationService(mem.Db),
            Sut(mem), new MoneyViewFactory(mem.Db, fx));
    }

    private static async Task ActivateAsync(SqliteInMemory mem, string preset)
    {
        foreach (var s in mem.Db.AllocationSchemes) s.IsActive = false;
        await mem.Db.SaveChangesAsync();

        mem.Db.AllocationSchemes.Add(AllocationPresets.Find(preset)!.ToScheme(isActive: true));
        mem.Db.Budgets.Add(new Budget { MonthlyAmount = Budget, UpdatedAt = DateTimeOffset.UtcNow });
        await mem.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Every_non_spending_bucket_becomes_a_pot_money_can_go_into()
    {
        using var mem = new SqliteInMemory();
        // 60% зобовʼязання / 10 пенсія / 10 довгі заощадження / 10 нерегулярні / 10 розваги
        await ActivateAsync(mem, "60-solution");

        var envelopes = await Sut(mem).StatusAsync(Budget);
        var names = envelopes.Select(e => e.Name).ToList();

        Assert.Contains("Пенсія", names);
        Assert.Contains("Нерегулярні витрати", names);
        Assert.DoesNotContain("Розваги", names); // spending money is not put aside
        Assert.Equal(600m, envelopes.Single(e => e.Name == "Пенсія").MonthGoal);
    }

    [Fact]
    public async Task The_default_pot_exists_even_with_no_scheme_at_all()
    {
        using var mem = new SqliteInMemory();
        mem.Db.Budgets.Add(new Budget { MonthlyAmount = Budget, UpdatedAt = DateTimeOffset.UtcNow });
        await mem.Db.SaveChangesAsync();

        var envelopes = await Sut(mem).StatusAsync(Budget);

        // Otherwise there would be nowhere to put money until a scheme is chosen.
        var def = Assert.Single(envelopes, e => e.IsDefault);
        Assert.Equal("Заощадження", def.Name);
    }

    [Fact]
    public async Task The_scheme_fills_the_pension_pot_by_itself()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        // Reading the status is enough: choosing a scheme is the decision, carrying it out
        // is not a chore to hand back to the user every month.
        var pension = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");

        Assert.Equal(600m, pension.Balance);
        Assert.Equal(600m, pension.DepositedThisMonth);
        Assert.Equal(0m, pension.StillToReserve); // nothing is "still to move" any more
        Assert.Equal(600m, pension.HeldBack);
    }

    [Fact]
    public async Task Reading_the_status_twice_does_not_fill_the_pot_twice()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        await Sut(mem).StatusAsync(Budget);
        await Sut(mem).StatusAsync(Budget);

        var pension = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");
        Assert.Equal(600m, pension.Balance);
    }

    /// A second invoice raises the budget, so the goal rises with it. The app keeps its own
    /// deposit in step instead of leaving a trail of correcting top-ups.
    [Fact]
    public async Task A_bigger_budget_raises_what_the_app_put_aside()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        await Sut(mem).StatusAsync(Budget);
        var pension = (await Sut(mem).StatusAsync(Budget * 2)).Single(e => e.Name == "Пенсія");

        Assert.Equal(1_200m, pension.Balance);
        Assert.Equal(1_200m, pension.DepositedThisMonth);
    }

    [Fact]
    public async Task A_deposit_by_hand_is_extra_on_top_of_the_plan()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");
        await Savings(mem).AddEntryAsync(new("Deposit", 250m, null, null, null, pension.Id));

        var after = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");

        // Moving money in by hand used to be the only way to meet the goal, so it counted
        // towards it. The app meets the goal now, so a hand-made deposit means "more than
        // planned" — and costs that much more of what is safe to spend.
        Assert.Equal(850m, after.Balance);
        Assert.Equal(850m, after.HeldBack);
    }

    [Fact]
    public async Task Money_cannot_be_taken_out_of_a_pot_it_was_never_put_into()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var all = await Sut(mem).StatusAsync(Budget);
        var pension = all.Single(e => e.Name == "Пенсія");
        var savings = all.Single(e => e.IsDefault);

        await Savings(mem).AddEntryAsync(new("Deposit", 500m, null, null, null, pension.Id));

        // A full pension envelope must not fund a withdrawal from an empty savings one.
        var r = await Savings(mem).AddEntryAsync(new("Withdrawal", 100m, null, null, null, savings.Id));

        Assert.False(r.IsSuccess);
    }

    [Fact]
    public async Task Starting_mid_month_stands_the_goals_down_but_keeps_the_balances()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        await Savings(mem).AddEntryAsync(new("Deposit", 400m, yesterday, null, null, pension.Id));

        // 1800 left to LIVE on, counted today. Reserving another 10% of it for four pots
        // would drop the daily norm to almost nothing — the exact thing the opening balance
        // is there to fix. The 400 put aside yesterday is already outside that 1800, and
        // what the app itself had set aside on paper this period is taken back out.
        var after = await Sut(mem).StatusAsync(1_800m, DateOnly.FromDateTime(DateTime.Now));

        Assert.All(after, e => Assert.Equal(0m, e.HeldBack));
        Assert.Equal(400m, after.Single(e => e.Name == "Пенсія").Balance);
    }

    [Fact]
    public async Task A_pot_left_behind_by_an_old_scheme_keeps_its_balance_and_stops_reserving()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");
        await Savings(mem).AddEntryAsync(new("Deposit", 400m, null, null, null, pension.Id));

        await ActivateAsync(mem, "80-20"); // no pension bucket any more

        var after = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");
        Assert.Equal(400m, after.Balance);  // the money did not evaporate with the scheme
        Assert.Equal(0m, after.MonthGoal);  // but it no longer takes anything from the norm
        Assert.Equal(0m, after.StillToReserve);
    }
}
