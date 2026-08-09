using FinanceApp.Application.Debts;
using static FinanceApp.Api.Tests.TestIncome;
using FinanceApp.Application.Common;
using Microsoft.Extensions.Logging.Abstractions;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Envelopes;
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
            new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db))),
            new EnvelopeService(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db), fx, new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db)), NullLogger<EnvelopeService>.Instance),
            new AllocationService(mem.Db),
            new MoneyViewFactory(mem.Db, fx),
            new BudgetPeriodResolver(mem.Db),
            new CarryoverService(
                mem.Db, new BudgetPeriodResolver(mem.Db),
                new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db))),
                NullLogger<CarryoverService>.Instance), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db)));
    }

    /// The default envelope is what the savings plan feeds and what these tests are about.
    private static EnvelopeSummary Savings(SafeToSpendResponse r) => r.Envelopes.Single(e => e.IsDefault);

    private static async Task ActivateAsync(SqliteInMemory mem, string preset)
    {
        foreach (var s in mem.Db.AllocationSchemes) s.IsActive = false;
        await mem.Db.SaveChangesAsync(); // released before the new one claims the unique index

        mem.Db.AllocationSchemes.Add(AllocationPresets.Find(preset)!.ToScheme(isActive: true));
        mem.Db.Transactions.Add(Income(Budget));
        await mem.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Default_scheme_leaves_the_whole_budget_spendable()
    {
        using var mem = new SqliteInMemory();
        mem.Db.Transactions.Add(Income(Budget));
        await mem.Db.SaveChangesAsync();

        var r = await Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(Budget, r.RemainingThisPeriod);
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
        Assert.Equal(4_200m, r.RemainingThisPeriod);
        Assert.Equal(1_800m, r.Allocation!.Reserved);
        Assert.Equal(1_200m, Savings(r).MonthGoal); // the scheme's 20%
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
        Assert.Equal(1_200m, Savings(r).MonthGoal);
        Assert.Equal(4_800m, r.RemainingThisPeriod);
    }

    [Fact]
    public async Task A_deposit_made_by_hand_is_held_back_on_top_of_the_scheme()
    {
        using var mem = new SqliteInMemory();
        await ActivateAsync(mem, "80-20");

        var fx = new FakeFxConverter();
        var savings = new SavingsService(
            mem.Db, new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db))), fx, new AllocationService(mem.Db),
            new EnvelopeService(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db), fx, new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db)), NullLogger<EnvelopeService>.Instance), new MoneyViewFactory(mem.Db, fx), NullLogger<SavingsService>.Instance);
        await savings.AddEntryAsync(new("Deposit", 500m, null, null, null));

        var r = await Sut(mem).GetSafeToSpendAsync();

        // 1200 the scheme puts aside by itself + 500 moved in by hand on top of it.
        Assert.Equal(1_700m, Savings(r).DepositedThisMonth);
        Assert.Equal(0m, Savings(r).StillToReserve);
        // And the extra 500 really is out of reach: 6000 − 1200 − 500.
        Assert.Equal(4_300m, r.RemainingThisPeriod);
    }
}
