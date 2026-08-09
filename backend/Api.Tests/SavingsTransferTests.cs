using FinanceApp.Application.Debts;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FinanceApp.Api.Tests.TestIncome;

namespace FinanceApp.Api.Tests;

/// Transfers between jars. By hand this was two movements — withdraw here, deposit there — and
/// in between the money existed nowhere; forget the second one and it stayed that way.
public class SavingsTransferTests
{
    private static EnvelopeService Envelopes(SqliteInMemory mem) =>
        new(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db),
            new FakeFxConverter(), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db)), NullLogger<EnvelopeService>.Instance);

    private static SavingsService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SavingsService(
            mem.Db, new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db), new DebtLedger(mem.Db, new BudgetPeriodResolver(mem.Db))), fx,
            new AllocationService(mem.Db), Envelopes(mem), new MoneyViewFactory(mem.Db, fx),
            NullLogger<SavingsService>.Instance);
    }

    private static MonthlyBudgetResult Month() => new(
        6_000m, null, BudgetPeriods.For(DateOnly.FromDateTime(DateTime.Now), BudgetPeriods.FirstOfMonth).Start, false);

    /// Two jars, with money in the first one.
    private static async Task<(int From, int To)> TwoJarsAsync(SqliteInMemory mem, decimal inFirst)
    {
        mem.Db.Transactions.Add(Income(6_000m));
        await mem.Db.SaveChangesAsync();

        var from = (await Envelopes(mem).CreateAsync("Заощадження", BucketKind.Savings)).Value!;
        var to = (await Envelopes(mem).CreateAsync("Відпустка", BucketKind.Savings)).Value!;
        await Sut(mem).AddEntryAsync(new("Deposit", inFirst, null, null, null, from.Id));
        return (from.Id, to.Id);
    }

    [Fact]
    public async Task Money_leaves_one_jar_and_arrives_in_the_other_in_one_act()
    {
        using var mem = new SqliteInMemory();
        var (from, to) = await TwoJarsAsync(mem, 1_000m);

        var moved = await Sut(mem).TransferAsync(new TransferRequest(from, to, 400m));

        Assert.True(moved.IsSuccess);
        var jars = await Envelopes(mem).StatusAsync(Month());
        Assert.Equal(600m, jars.Single(e => e.Id == from).Balance);
        Assert.Equal(400m, jars.Single(e => e.Id == to).Balance);
        // The total is what it was: a transfer neither creates nor eats money.
        Assert.Equal(1_000m, jars.Sum(e => e.Balance));
    }

    [Fact]
    public async Task More_than_the_jar_holds_does_not_move()
    {
        using var mem = new SqliteInMemory();
        var (from, to) = await TwoJarsAsync(mem, 300m);

        var refused = await Sut(mem).TransferAsync(new TransferRequest(from, to, 500m));

        Assert.False(refused.IsSuccess);
        Assert.Equal(ErrorType.Validation, refused.Error.Type);
        Assert.Contains("300", refused.Error.Message);
        Assert.Empty(await mem.Db.SavingsEntries.Where(x => x.TransferKey != null).ToListAsync());
    }

    [Fact]
    public async Task A_jar_cannot_move_money_to_itself()
    {
        using var mem = new SqliteInMemory();
        var (from, _) = await TwoJarsAsync(mem, 1_000m);

        var refused = await Sut(mem).TransferAsync(new TransferRequest(from, from, 100m));

        Assert.False(refused.IsSuccess);
    }

    /// Half a transfer is not a movement in itself: if only that half vanished, money would
    /// leave one jar and arrive in none — and "Відкладено всього" would report that as fact.
    [Fact]
    public async Task Undoing_a_transfer_takes_both_halves()
    {
        using var mem = new SqliteInMemory();
        var (from, to) = await TwoJarsAsync(mem, 1_000m);
        await Sut(mem).TransferAsync(new TransferRequest(from, to, 400m));

        var half = await mem.Db.SavingsEntries.FirstAsync(x => x.TransferKey != null);
        await Sut(mem).DeleteEntryAsync(half.Id);

        Assert.Empty(await mem.Db.SavingsEntries.Where(x => x.TransferKey != null).ToListAsync());
        var jars = await Envelopes(mem).StatusAsync(Month());
        Assert.Equal(1_000m, jars.Single(e => e.Id == from).Balance);
        Assert.Equal(0m, jars.Single(e => e.Id == to).Balance);
    }

    [Fact]
    public async Task Half_a_transfer_is_not_edited_on_its_own()
    {
        using var mem = new SqliteInMemory();
        var (from, to) = await TwoJarsAsync(mem, 1_000m);
        await Sut(mem).TransferAsync(new TransferRequest(from, to, 400m));

        var half = await mem.Db.SavingsEntries.FirstAsync(x => x.TransferKey != null);
        var refused = await Sut(mem).UpdateEntryAsync(half.Id, new("Deposit", 999m, null, null, null, null));

        Assert.False(refused.IsSuccess);
        Assert.Equal(400m, (await mem.Db.SavingsEntries.FindAsync(half.Id))!.AmountBase);
    }
}
