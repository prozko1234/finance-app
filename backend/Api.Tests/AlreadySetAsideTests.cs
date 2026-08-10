using FinanceApp.Application.Debts;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Common;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FinanceApp.Api.Tests.TestIncome;

namespace FinanceApp.Api.Tests;

/// Writing down a jar that was already full.
///
/// Every deposit used to be read as money leaving the current budget right now, so entering a
/// pot saved over a year took that whole amount off "скільки можна витратити today". A year's
/// savings typed in on one afternoon read as spending them that afternoon, and the daily
/// figure went deeply negative over money that was never in this period's income.
public class AlreadySetAsideTests
{
    private static async Task<Envelope> JarAsync(SqliteInMemory mem)
    {
        var jar = new Envelope { Name = "Подушка", Kind = BucketKind.Savings, IsDefault = true };
        mem.Db.Envelopes.Add(jar);
        mem.Db.Transactions.Add(Income(5_000m));
        await mem.Db.SaveChangesAsync();
        return jar;
    }

    private static SavingsEntry Deposit(int envelopeId, decimal amount, bool alreadySetAside)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return new SavingsEntry
        {
            EnvelopeId = envelopeId, Date = today, Kind = SavingsEntryKind.Deposit,
            CurrencyOriginal = "PLN", AmountOriginal = amount, AmountBase = amount,
            FxRate = 1m, FxDate = today, AlreadySetAside = alreadySetAside,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static SummaryService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SummaryService(
            mem.Db, fx,
            new RecurringMaterializer(mem.Db, fx, new BudgetPeriodResolver(mem.Db)),
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

    /// The reported case, in numbers: a jar holding a year of savings is written down, and the
    /// month it is written down in must not pay for it.
    [Fact]
    public async Task Recording_a_jar_that_was_already_full_costs_the_budget_nothing()
    {
        using var mem = new SqliteInMemory();
        var jar = await JarAsync(mem);

        var before = await Sut(mem).GetSafeToSpendAsync();

        mem.Db.SavingsEntries.Add(Deposit(jar.Id, 7_000m, alreadySetAside: true));
        await mem.Db.SaveChangesAsync();

        var after = await Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(before.RemainingThisPeriod, after.RemainingThisPeriod);
        Assert.Equal(before.DailyNorm, after.DailyNorm);

        // And the jar really does hold it — the money is not ignored, only its origin is.
        Assert.Equal(7_000m, after.Envelopes.Single(e => e.Id == jar.Id).Balance);
    }

    /// The other half of the rule, or the fix would just be a way to hide spending: money put
    /// aside out of this period's income still leaves the daily norm.
    [Fact]
    public async Task Putting_money_aside_now_still_costs_the_budget()
    {
        using var mem = new SqliteInMemory();
        var jar = await JarAsync(mem);

        var before = await Sut(mem).GetSafeToSpendAsync();

        mem.Db.SavingsEntries.Add(Deposit(jar.Id, 500m, alreadySetAside: false));
        await mem.Db.SaveChangesAsync();

        var after = await Sut(mem).GetSafeToSpendAsync();

        Assert.Equal(before.RemainingThisPeriod - 500m, after.RemainingThisPeriod);
    }

    /// A withdrawal is a movement whenever it happened — taking money out of a jar puts it
    /// back within reach, and there is no "this was already taken out" to claim.
    [Fact]
    public async Task Only_a_deposit_can_be_money_that_was_already_put_away()
    {
        using var mem = new SqliteInMemory();
        var jar = await JarAsync(mem);
        mem.Db.SavingsEntries.Add(Deposit(jar.Id, 1_000m, alreadySetAside: true));
        await mem.Db.SaveChangesAsync();

        var fx = new FakeFxConverter();
        var savings = new SavingsService(
            mem.Db,
            new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db))),
            fx,
            new AllocationService(mem.Db),
            new EnvelopeService(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db), fx, new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db)), NullLogger<EnvelopeService>.Instance),
            new MoneyViewFactory(mem.Db, fx),
            NullLogger<SavingsService>.Instance);

        var result = await savings.AddEntryAsync(new Application.Contracts.SaveSavingsEntryRequest(
            "Withdrawal", 100m, null, null, EnvelopeId: jar.Id, AlreadySetAside: true));

        Assert.True(result.IsSuccess);
        var stored = await mem.Db.SavingsEntries
            .SingleAsync(x => x.Kind == SavingsEntryKind.Withdrawal);
        Assert.False(stored.AlreadySetAside);
    }
}
