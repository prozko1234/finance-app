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
        new(mem.Db, new AllocationService(mem.Db));

    private static SavingsService Savings(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SavingsService(
            mem.Db, new MonthlyBudget(mem.Db), fx, new AllocationService(mem.Db),
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
    public async Task A_deposit_into_the_pension_pot_builds_a_balance_without_reserving_twice()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "60-solution");

        var pension = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");
        Assert.Equal(600m, pension.HeldBack); // nothing moved yet: the whole goal is held

        await Savings(mem).AddEntryAsync(new("Deposit", 250m, null, null, null, pension.Id));

        var after = (await Sut(mem).StatusAsync(Budget)).Single(e => e.Name == "Пенсія");
        Assert.Equal(250m, after.Balance);
        Assert.Equal(250m, after.DepositedThisMonth);
        Assert.Equal(350m, after.StillToReserve);
        // The key property: moving 250 by hand does not cost another 250 of safe-to-spend.
        Assert.Equal(600m, after.HeldBack);
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
