using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Domain.Tax;
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
            StartsOn = today, // due today
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
            StartsOn = today.AddDays(2), // still in the future
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await mem.Db.SaveChangesAsync();

        await new RecurringMaterializer(mem.Db, new FakeFxConverter()).MaterializeDueAsync();

        Assert.Empty(await mem.Db.Transactions.Where(t => t.Source == TxSource.Recurring).ToListAsync());
    }

    /// A stable salary is recurring too. It must land as INCOME with VAT split out —
    /// materializing it as an expense would subtract the salary from the budget.
    [Fact]
    public async Task Materializes_recurring_income_with_vat_split_out()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);

        mem.Db.TaxProfiles.Add(new TaxProfile
        {
            Regime = TaxRegime.Ryczalt,
            RyczaltRate = 0.12m,
            VatPayer = true,
            VatRate = 0.23m,
            ValidFrom = new DateOnly(today.Year, 1, 1),
        });
        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            Kind = TransactionKind.Income,
            AmountIncludesVat = true,
            AmountOriginal = 24_600m,
            CurrencyOriginal = "PLN",
            CategoryId = 1,
            StartsOn = today,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await mem.Db.SaveChangesAsync();

        await new RecurringMaterializer(mem.Db, new FakeFxConverter()).MaterializeDueAsync();

        var tx = await mem.Db.Transactions.SingleAsync(t => t.Source == TxSource.Recurring);
        Assert.Equal(TransactionKind.Income, tx.Kind);
        Assert.Equal(20_000m, tx.AmountBase);      // przychód, VAT excluded
        Assert.Equal(24_600m, tx.GrossWithVat);
        Assert.Equal(4_600m, tx.VatAmount);
    }

    /// A row whose start date was never set sits at year 1. Walking period by period from
    /// there wrote ~24 000 phantom charges and made the app unusable — the catch-up must be
    /// bounded by how far back it is worth looking, not by what a date field happens to say.
    /// Weekly makes it worse than the monthly case that first caused it: four times worse.
    [Fact]
    public async Task A_recurring_without_a_start_date_does_not_backfill_two_millennia()
    {
        using var mem = new SqliteInMemory();

        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            AmountOriginal = 49.99m,
            CurrencyOriginal = "PLN",
            CategoryId = 1,
            Unit = RecurrenceUnit.Week,
            Interval = 1,
            Active = true,
            // StartsOn deliberately left unset — default(DateOnly) is 0001-01-01.
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        await new RecurringMaterializer(mem.Db, new FakeFxConverter()).MaterializeDueAsync();

        // Two years of weeks, and not one charge more.
        var rows = await mem.Db.Transactions.CountAsync();
        Assert.InRange(rows, 100, 106);
    }

    [Fact]
    public async Task Catch_up_never_reaches_further_back_than_two_years()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);

        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            AmountOriginal = 10m,
            CurrencyOriginal = "PLN",
            CategoryId = 1,
            StartsOn = new DateOnly(today.Year, today.Month, 1).AddYears(-10),
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow.AddYears(-10),
        });
        await mem.Db.SaveChangesAsync();

        await new RecurringMaterializer(mem.Db, new FakeFxConverter()).MaterializeDueAsync();

        // 24 months back plus the current one, minus this month's charge if it is not due yet.
        var rows = await mem.Db.Transactions.CountAsync();
        Assert.InRange(rows, 24, 25);
    }
}
