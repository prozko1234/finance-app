using FinanceApp.Application.Allocations;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Tax;
using FinanceApp.Domain;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// The engine computes ZUS, the health contribution and PIT from a profile, and it is right
/// often enough to be worth having. It is still a model: the figure that gets paid comes from
/// a person with the full picture — a month with a sick note, a deduction the app knows nothing
/// about, a rate that changed before the code did. Until now the only way to reconcile the two
/// was to stop believing the app.
public class TaxActualsTests
{
    private static readonly DateOnly ThisMonth =
        new(DateOnly.FromDateTime(DateTime.Now).Year, DateOnly.FromDateTime(DateTime.Now).Month, 1);

    [Fact]
    public async Task What_the_bookkeeper_said_wins_over_what_the_engine_worked_out()
    {
        using var mem = new SqliteInMemory();
        await RyczaltWithIncomeAsync(mem, 10_000m);

        var computed = await Budget(mem).ResolveAsync();
        Assert.NotNull(computed.Taxes);

        await Tax(mem).SaveActualsAsync(new SaveTaxActualsRequest(ThisMonth, ZusSocial: 1_000m, Health: null, Pit: null));

        var after = await Budget(mem).ResolveAsync();

        Assert.Equal(1_000m, after.Taxes!.ZusSocial);
        // And it moved the money, which is the whole point of a correction.
        Assert.NotEqual(computed.Budget, after.Budget);
        Assert.Equal(
            after.Taxes.GrossWithVat - after.Taxes.VatAmount - 1_000m
                - after.Taxes.HealthContribution - after.Taxes.Tax,
            after.Budget);
    }

    /// The bookkeeper rarely hands over all three at once, so a month with only ZUS filled in
    /// keeps the engine's health and PIT rather than zeroing them.
    [Fact]
    public async Task A_component_left_empty_keeps_the_computed_one()
    {
        using var mem = new SqliteInMemory();
        await RyczaltWithIncomeAsync(mem, 10_000m);

        var before = (await Budget(mem).ResolveAsync()).Taxes!;
        await Tax(mem).SaveActualsAsync(new SaveTaxActualsRequest(ThisMonth, ZusSocial: 1_000m, Health: null, Pit: null));
        var after = (await Budget(mem).ResolveAsync()).Taxes!;

        Assert.Equal(before.HealthContribution, after.HealthContribution);
        Assert.Equal(before.Tax, after.Tax);
    }

    /// Clearing every field is how "use the engine's figures again" is said, and an all-null
    /// override left lying about would be a row that means nothing.
    [Fact]
    public async Task Clearing_every_field_gives_the_engine_its_month_back()
    {
        using var mem = new SqliteInMemory();
        await RyczaltWithIncomeAsync(mem, 10_000m);

        var computed = (await Budget(mem).ResolveAsync()).Budget;

        await Tax(mem).SaveActualsAsync(new SaveTaxActualsRequest(ThisMonth, 1_000m, 500m, 400m));
        Assert.NotEqual(computed, (await Budget(mem).ResolveAsync()).Budget);

        await Tax(mem).SaveActualsAsync(new SaveTaxActualsRequest(ThisMonth, null, null, null));

        Assert.Equal(computed, (await Budget(mem).ResolveAsync()).Budget);
        Assert.Empty(await mem.Db.TaxActuals.ToListAsync());
    }

    /// The form shows the engine's figure as the thing being corrected, so it has to come back
    /// beside whatever was typed over it.
    [Fact]
    public async Task The_engines_own_figures_come_back_beside_the_saved_ones()
    {
        using var mem = new SqliteInMemory();
        await RyczaltWithIncomeAsync(mem, 10_000m);

        await Tax(mem).SaveActualsAsync(new SaveTaxActualsRequest(ThisMonth, ZusSocial: 1_000m, Health: null, Pit: null));
        var r = await Tax(mem).GetActualsAsync(ThisMonth);

        Assert.Equal(1_000m, r.ZusSocial);
        Assert.Null(r.Health);
        Assert.True(r.ComputedZusSocial > 0);
        Assert.NotEqual(1_000m, r.ComputedZusSocial);
        Assert.True(r.ComputedHealth > 0);
    }

    /// A month with nothing in it owes nothing, and asking about one must not fail.
    [Fact]
    public async Task A_month_with_no_income_owes_nothing()
    {
        using var mem = new SqliteInMemory();

        var r = await Tax(mem).GetActualsAsync(ThisMonth);

        Assert.Equal(0m, r.ComputedZusSocial);
        Assert.Equal(0m, r.ComputedPit);
        Assert.Null(r.ZusSocial);
    }

    [Fact]
    public async Task A_negative_figure_is_refused()
    {
        using var mem = new SqliteInMemory();

        var r = await Tax(mem).SaveActualsAsync(new SaveTaxActualsRequest(ThisMonth, ZusSocial: -1m, Health: null, Pit: null));

        Assert.False(r.IsSuccess);
    }

    /// Any day of the month names the month. A figure saved on the 20th and read back on the
    /// 3rd is the same figure.
    [Fact]
    public async Task Any_day_of_the_month_names_the_same_month()
    {
        using var mem = new SqliteInMemory();
        await RyczaltWithIncomeAsync(mem, 10_000m);

        await Tax(mem).SaveActualsAsync(new SaveTaxActualsRequest(ThisMonth.AddDays(19), 1_000m, null, null));
        var r = await Tax(mem).GetActualsAsync(ThisMonth.AddDays(2));

        Assert.Equal(1_000m, r.ZusSocial);
        Assert.Equal(ThisMonth, r.Month);
    }

    private static async Task RyczaltWithIncomeAsync(SqliteInMemory mem, decimal revenue)
    {
        mem.Db.TaxProfiles.Add(new TaxProfile
        {
            Regime = TaxRegime.Ryczalt,
            RyczaltRate = 0.12m,
            VatPayer = false,
            ZusType = ZusType.Duzy,
            // Ryczalt takes the contributions from the profile itself — they are a monthly
            // flat figure, not something derived from the invoice.
            ZusSocial = 1_600m,
            HealthContribution = 460m,
        });
        mem.Db.Transactions.Add(TestIncome.Income(revenue));
        await mem.Db.SaveChangesAsync();
    }

    private static TaxService Tax(SqliteInMemory mem) =>
        new(mem.Db, new BudgetPeriodResolver(mem.Db), new AllocationService(mem.Db));

    private static Application.Summaries.MonthlyBudget Budget(SqliteInMemory mem)
    {
        var periods = new BudgetPeriodResolver(mem.Db);
        return new Application.Summaries.MonthlyBudget(
            mem.Db, periods, new Application.Debts.DebtLedger(mem.Db, periods));
    }
}
