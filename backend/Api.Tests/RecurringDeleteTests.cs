using FinanceApp.Application.Common;
using FinanceApp.Application.Display;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Transactions;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// Deleting a charge that a subscription wrote used to be undone by the app itself: the next
/// read materialized it again, because "has this already been charged?" is answered by
/// looking for the very row that was just removed. On screen it looked like the last expense
/// refusing to be deleted — and coming back with a new id.
public class RecurringDeleteTests
{
    private static async Task<(SqliteInMemory Mem, RecurringExpense Rule)> SetUpAsync(DateOnly startsOn)
    {
        var mem = new SqliteInMemory();
        mem.Db.Categories.Add(new Category { Name = "Розваги" });
        await mem.Db.SaveChangesAsync();

        var rule = new RecurringExpense
        {
            AmountOriginal = 49.99m,
            CurrencyOriginal = "PLN",
            CategoryId = (await mem.Db.Categories.FirstAsync()).Id,
            StartsOn = startsOn,
            Unit = RecurrenceUnit.Month,
            Interval = 1,
            Active = true,
            Note = "Netflix",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.RecurringExpenses.Add(rule);
        await mem.Db.SaveChangesAsync();

        return (mem, rule);
    }

    private static RecurringMaterializer Materializer(SqliteInMemory mem) =>
        new(mem.Db, new FakeFxConverter());

    private static TransactionService Transactions(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new TransactionService(mem.Db, fx, new RecurringMaterializer(mem.Db, fx),
            new MoneyViewFactory(mem.Db, fx));
    }

    [Fact]
    public async Task A_deleted_subscription_charge_does_not_come_back()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (mem, _) = await SetUpAsync(today);
        using var _mem = mem;

        await Materializer(mem).MaterializeDueAsync();
        var charge = await mem.Db.Transactions.SingleAsync();

        var deleted = await Transactions(mem).DeleteAsync(charge.Id);
        Assert.True(deleted.IsSuccess);

        // The read that used to resurrect it.
        await Materializer(mem).MaterializeDueAsync();

        Assert.Empty(await mem.Db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task Deleting_one_occurrence_leaves_the_others_alone()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        // Three months back, so there are several occurrences by now.
        var (mem, _) = await SetUpAsync(today.AddMonths(-3));
        using var _mem = mem;

        await Materializer(mem).MaterializeDueAsync();
        var all = await mem.Db.Transactions.OrderBy(t => t.Date).ToListAsync();
        Assert.True(all.Count >= 3);

        await Transactions(mem).DeleteAsync(all[1].Id);
        await Materializer(mem).MaterializeDueAsync();

        var left = await mem.Db.Transactions.OrderBy(t => t.Date).ToListAsync();
        Assert.Equal(all.Count - 1, left.Count);
        Assert.DoesNotContain(left, t => t.Date == all[1].Date);
    }

    /// The subscription itself is untouched: refusing one month's charge is not the same as
    /// cancelling the subscription, and quietly doing the second would lose the rule.
    [Fact]
    public async Task The_rule_keeps_running_after_one_charge_is_refused()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (mem, rule) = await SetUpAsync(today.AddMonths(-2));
        using var _mem = mem;

        await Materializer(mem).MaterializeDueAsync();
        var first = await mem.Db.Transactions.OrderBy(t => t.Date).FirstAsync();
        await Transactions(mem).DeleteAsync(first.Id);

        var stillThere = await mem.Db.RecurringExpenses.FindAsync(rule.Id);
        Assert.NotNull(stillThere);
        Assert.True(stillThere!.Active);
    }

    /// A manual expense has no rule behind it, so deleting one must not write a skip that
    /// would sit in the table forever meaning nothing.
    [Fact]
    public async Task Deleting_an_ordinary_expense_records_nothing()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var (mem, _) = await SetUpAsync(today);
        using var _mem = mem;

        var manual = new Transaction
        {
            AmountOriginal = 10m, CurrencyOriginal = "PLN", AmountBase = 10m,
            FxRate = 1m, FxDate = today, CategoryId = 1, Date = today,
            Frequency = Frequency.OneOff, Source = TxSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        mem.Db.Transactions.Add(manual);
        await mem.Db.SaveChangesAsync();

        await Transactions(mem).DeleteAsync(manual.Id);

        Assert.Empty(await mem.Db.RecurringSkips.ToListAsync());
    }
}
