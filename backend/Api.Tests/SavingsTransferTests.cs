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

/// Перекидання між банками. Руками це були два рухи — зняти тут, покласти там, — і між ними
/// гроші не існували ніде; а якщо другий рух забували, вони там і лишались.
public class SavingsTransferTests
{
    private static EnvelopeService Envelopes(SqliteInMemory mem) =>
        new(mem.Db, new AllocationService(mem.Db), new BudgetPeriodResolver(mem.Db),
            new FakeFxConverter(), NullLogger<EnvelopeService>.Instance);

    private static SavingsService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new SavingsService(
            mem.Db, new MonthlyBudget(mem.Db, new BudgetPeriodResolver(mem.Db)), fx,
            new AllocationService(mem.Db), Envelopes(mem), new MoneyViewFactory(mem.Db, fx),
            NullLogger<SavingsService>.Instance);
    }

    private static MonthlyBudgetResult Month() => new(
        6_000m, null, BudgetPeriods.For(DateOnly.FromDateTime(DateTime.Now), BudgetPeriods.FirstOfMonth).Start, false);

    /// Дві банки з грошима в першій.
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
        // Разом — стільки ж, скільки було: перекидання не створює і не з'їдає грошей.
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

    /// Половина перекидання — не рух сам по собі: якби зникла лише вона, гроші пішли б із
    /// однієї банки й не прийшли б у жодну, а «Відкладено всього» показало б це як факт.
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
