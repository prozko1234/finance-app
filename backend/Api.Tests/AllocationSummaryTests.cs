using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Display;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;

namespace FinanceApp.Api.Tests;

/// The scheme meets the rest of the app: it must shrink what may be spent, and it must
/// not reserve for savings twice when a savings plan also exists.
public class AllocationSummaryTests
{
    private const decimal Budget = 6_000m;

    private static SummaryService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SummaryService(
            mem.Db, fx,
            new RecurringMaterializer(mem.Db, fx),
            new MonthlyBudget(mem.Db),
            new SavingsService(mem.Db, new MonthlyBudget(mem.Db), fx, new AllocationService(mem.Db), new MoneyViewFactory(mem.Db, fx)),
            new AllocationService(mem.Db),
            new MoneyViewFactory(mem.Db, fx));
    }

    private static async Task ActivateAsync(SqliteInMemory mem, string preset)
    {
        foreach (var s in mem.Db.AllocationSchemes) s.IsActive = false;
        await mem.Db.SaveChangesAsync(); // released before the new one claims the unique index

        mem.Db.AllocationSchemes.Add(AllocationPresets.Find(preset)!.ToScheme(isActive: true));
        mem.Db.Budgets.Add(new Budget { MonthlyAmount = Budget, UpdatedAt = DateTimeOffset.UtcNow });
        await mem.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Default_scheme_leaves_the_whole_budget_spendable()
    {
        using var mem = new SqliteInMemory();
        mem.Db.Budgets.Add(new Budget { MonthlyAmount = Budget, UpdatedAt = DateTimeOffset.UtcNow });
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(Budget, r.RemainingThisMonth);
        Assert.Equal(AllocationPresets.DailyNormOnly, r.Allocation!.Preset);
        Assert.Equal(Budget, r.Allocation.Spendable);
        Assert.Equal(0m, r.Allocation.Reserved);
    }

    [Fact]
    public async Task Non_spending_buckets_are_held_back_from_the_daily_norm()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "70-20-10"); // 70 spending / 20 savings / 10 debt

        var r = await Sut(mem).GetSafeToSpendAsync();

        // 70% spendable; the 20% savings and 10% debt never reach the norm.
        Assert.Equal(4_200m, r.RemainingThisMonth);
        Assert.Equal(1_800m, r.Allocation!.Reserved);
        Assert.Equal(1_200m, r.Savings.MonthGoal); // the scheme's 20%
    }

    [Fact]
    public async Task A_savings_bucket_replaces_the_plan_instead_of_reserving_on_top_of_it()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "80-20"); // 20% savings = 1200
        mem.Db.SavingsPlans.Add(new SavingsPlan
        {
            Mode = SavingsMode.Fixed, Value = 2_000m, Active = true, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetSafeToSpendAsync();

        // The plan's 2000 is ignored: 1200 is reserved once, not 1200 + 2000.
        Assert.Equal(1_200m, r.Savings.MonthGoal);
        Assert.Equal(4_800m, r.RemainingThisMonth);
    }

    [Fact]
    public async Task A_deposit_made_this_month_eats_into_the_scheme_reservation()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "80-20");

        var fx = new FakeFxConverter();
        var savings = new SavingsService(
            mem.Db, new MonthlyBudget(mem.Db), fx, new AllocationService(mem.Db), new MoneyViewFactory(mem.Db, fx));
        await savings.AddEntryAsync(new("Deposit", 500m, null, null, null));

        var r = await Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(500m, r.Savings.DepositedThisMonth);
        Assert.Equal(700m, r.Savings.StillToReserve);
        // Still 4800 spendable: the 500 left the budget as a deposit, not as a second reserve.
        Assert.Equal(4_800m, r.RemainingThisMonth);
    }
}
