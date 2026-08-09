using FinanceApp.Application.Debts;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Common;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;
using FinanceApp.Api.Tests.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceApp.Api.Tests;

/// The money left when a period ends. It used to evaporate: a new period's budget is the new
/// income, so anything underspent existed only in the bank account. These tests pin down that
/// it is found, offered once, and lands where the user said.
public class CarryoverTests
{
    private static CarryoverService Sut(SqliteInMemory mem)
    {
        var periods = new BudgetPeriodResolver(mem.Db);
        return new CarryoverService(
            mem.Db, periods, new MonthlyBudget(mem.Db, periods, new DebtLedger(mem.Db, periods)), NullLogger<CarryoverService>.Instance);
    }

    /// Everything is dated in the PREVIOUS calendar month, with the period running from the
    /// 1st — the default. Today's month is the one that inherits.
    private static DateOnly LastMonth => DateOnly.FromDateTime(DateTime.Now).AddMonths(-1);

    private static async Task<int> SetUpAsync(SqliteInMemory mem, decimal income, decimal spent)
    {
        var category = new Category { Name = "Їжа" };
        mem.Db.Categories.Add(category);
        mem.Db.Envelopes.Add(new Envelope
        {
            Name = Envelope.DefaultName, Kind = BucketKind.Savings, IsDefault = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var on = new DateOnly(LastMonth.Year, LastMonth.Month, 5);
        mem.Db.Transactions.AddRange(
            Tx(income, on, category.Id, TransactionKind.Income),
            Tx(spent, on, category.Id, TransactionKind.Expense));
        await mem.Db.SaveChangesAsync();
        return category.Id;
    }

    private static Transaction Tx(decimal amount, DateOnly date, int categoryId, TransactionKind kind) =>
        new()
        {
            Kind = kind, CurrencyOriginal = "PLN", AmountOriginal = amount, AmountBase = amount,
            FxRate = 1m, FxDate = date, Date = date, CategoryId = categoryId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Offers_what_the_last_period_did_not_spend()
    {
        using var mem = new SqliteInMemory();
        await SetUpAsync(mem, income: 8_000m, spent: 6_500m);

        var pending = await Sut(mem).PendingAsync();

        Assert.NotNull(pending);
        Assert.Equal(1_500m, pending.Amount);
        Assert.Equal(Envelope.DefaultName, pending.EnvelopeName);
    }

    /// Money already moved into a jar left the spendable pile when it went in. Counting it
    /// again here would offer the same money to be saved twice.
    [Fact]
    public async Task Does_not_offer_money_that_already_went_into_a_jar()
    {
        using var mem = new SqliteInMemory();
        await SetUpAsync(mem, income: 8_000m, spent: 6_500m);
        var jar = await mem.Db.Envelopes.FirstAsync();

        mem.Db.SavingsEntries.Add(new SavingsEntry
        {
            EnvelopeId = jar.Id, Date = new DateOnly(LastMonth.Year, LastMonth.Month, 6),
            Kind = SavingsEntryKind.Deposit,
            AmountOriginal = 1_000m, CurrencyOriginal = "PLN", AmountBase = 1_000m,
            FxRate = 1m, FxDate = new DateOnly(LastMonth.Year, LastMonth.Month, 6),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        Assert.Equal(500m, (await Sut(mem).PendingAsync())!.Amount);
    }

    [Fact]
    public async Task Says_nothing_when_the_period_was_overspent()
    {
        using var mem = new SqliteInMemory();
        await SetUpAsync(mem, income: 8_000m, spent: 9_000m);

        Assert.Null(await Sut(mem).PendingAsync());
    }

    [Fact]
    public async Task Putting_it_in_a_jar_deposits_it_and_ends_the_question()
    {
        using var mem = new SqliteInMemory();
        await SetUpAsync(mem, income: 8_000m, spent: 6_500m);
        var sut = Sut(mem);

        var r = await sut.DecideAsync(CarryoverDecision.ToEnvelope, envelopeId: null);

        Assert.True(r.IsSuccess);
        var entry = await mem.Db.SavingsEntries.SingleAsync();
        Assert.Equal(1_500m, entry.AmountBase);
        Assert.False(entry.IsAuto); // the scheme must stay free to re-pour its own entry
        Assert.Null(await sut.PendingAsync());
    }

    /// "Не рахувати" is an answer, and an answer is what stops the card coming back — the row
    /// is written even though no money moves.
    [Fact]
    public async Task Ignoring_it_moves_nothing_and_still_ends_the_question()
    {
        using var mem = new SqliteInMemory();
        await SetUpAsync(mem, income: 8_000m, spent: 6_500m);
        var sut = Sut(mem);

        Assert.True((await sut.DecideAsync(CarryoverDecision.Ignore, null)).IsSuccess);

        Assert.Empty(await mem.Db.SavingsEntries.ToListAsync());
        Assert.Null(await sut.PendingAsync());
    }

    [Fact]
    public async Task Keeping_it_for_spending_raises_this_periods_budget()
    {
        using var mem = new SqliteInMemory();
        var category = await SetUpAsync(mem, income: 8_000m, spent: 6_500m);
        var periods = new BudgetPeriodResolver(mem.Db);
        var budget = new MonthlyBudget(mem.Db, periods, new DebtLedger(mem.Db, periods));

        // This period has an income of its own, so the leftover has to be visible on top of it.
        mem.Db.Transactions.Add(Tx(
            10_000m, DateOnly.FromDateTime(DateTime.Now), category, TransactionKind.Income));
        await mem.Db.SaveChangesAsync();

        Assert.Equal(10_000m, (await budget.ResolveAsync()).Budget);

        Assert.True((await Sut(mem).DecideAsync(CarryoverDecision.ToBudget, null)).IsSuccess);

        Assert.Equal(11_500m, (await budget.ResolveAsync()).Budget);
    }
}
