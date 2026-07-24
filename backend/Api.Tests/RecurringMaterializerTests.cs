using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

public class RecurringMaterializerTests
{
    [Fact]
    public async Task Materializes_due_recurring_once_and_is_idempotent()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);

        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            AmountOriginal = 50m,
            CurrencyOriginal = "PLN",
            CategoryId = 1,
            DayOfMonth = today.Day, // due today
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await mem.Db.SaveChangesAsync();

        var sut = new RecurringMaterializer(mem.Db, new FakeFxConverter());
        await sut.MaterializeDueAsync();
        await sut.MaterializeDueAsync(); // second run must not create a duplicate

        var txns = await mem.Db.Transactions
            .Where(t => t.Source == TxSource.Recurring)
            .ToListAsync();

        Assert.Single(txns);
        Assert.Equal(50m, txns[0].AmountBase);
        Assert.NotNull(txns[0].RecurringExpenseId);
    }

    [Fact]
    public async Task Does_not_materialize_a_future_charge()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today.Day >= 28) return; // skip near month end where "+2 days" could roll over

        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            AmountOriginal = 50m,
            CurrencyOriginal = "PLN",
            CategoryId = 1,
            DayOfMonth = today.Day + 2, // still in the future this month
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await mem.Db.SaveChangesAsync();

        await new RecurringMaterializer(mem.Db, new FakeFxConverter()).MaterializeDueAsync();

        Assert.Empty(await mem.Db.Transactions.Where(t => t.Source == TxSource.Recurring).ToListAsync());
    }
}
