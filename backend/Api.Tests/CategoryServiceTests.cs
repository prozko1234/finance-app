using FinanceApp.Application.Categories;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

public class CategoryServiceTests
{
    private const int FallbackId = 6; // "Інше", seeded as IsSystem

    [Fact]
    public async Task Creates_category_at_the_end_before_the_system_one()
    {
        using var mem = new SqliteInMemory();
        var sut = new CategoryService(mem.Db);

        var r = await sut.CreateAsync(new SaveCategoryRequest("Хобі", "🎨", "#059669"));

        Assert.True(r.IsSuccess);
        Assert.Equal("Хобі", r.Value!.Name);
        Assert.False(r.Value.IsSystem);

        var all = await sut.GetAllAsync();
        Assert.Equal("Інше", all[^1].Name); // system fallback stays last
    }

    /// The home screen's shortcut row. It used to be ranked on the client over whatever page
    /// of recent transactions was loaded, so a category abandoned months ago outranked one
    /// used daily this week — these tests pin the window that replaced that.
    [Fact]
    public async Task Ranks_shortcuts_by_use_inside_the_recent_window()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);

        // Транспорт is used more, but all of it is outside the fortnight AND the month.
        mem.Db.Transactions.AddRange(
            Expense(today.AddDays(-1), 1), Expense(today.AddDays(-2), 1), Expense(today.AddDays(-3), 1),
            Expense(today.AddDays(-4), 2), Expense(today.AddDays(-5), 3),
            Expense(today.AddDays(-90), 4), Expense(today.AddDays(-91), 4),
            Expense(today.AddDays(-92), 4), Expense(today.AddDays(-93), 4));
        await mem.Db.SaveChangesAsync();

        var r = await new CategoryService(mem.Db).GetFrequentAsync();

        Assert.Equal([1, 2, 3], r.Select(x => x.CategoryId));
        Assert.Equal(3, r[0].Uses);
        Assert.All(r, x => Assert.Equal(14, x.Days));
    }

    /// A row of one button is not worth the space, so a thin fortnight falls through to the
    /// month — and says which window it ended up using, because the screen prints it.
    [Fact]
    public async Task Widens_to_a_month_when_the_fortnight_is_too_thin()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);

        mem.Db.Transactions.AddRange(
            Expense(today.AddDays(-1), 1),
            Expense(today.AddDays(-20), 2), Expense(today.AddDays(-21), 3));
        await mem.Db.SaveChangesAsync();

        var r = await new CategoryService(mem.Db).GetFrequentAsync();

        Assert.Equal(3, r.Count);
        Assert.All(r, x => Assert.Equal(30, x.Days));
    }

    /// Income is entered a few times a month through its own flow; a shortcut for it would
    /// take a slot from something tapped daily.
    [Fact]
    public async Task Leaves_income_out_of_the_shortcuts()
    {
        using var mem = new SqliteInMemory();
        var today = DateOnly.FromDateTime(DateTime.Now);

        mem.Db.Transactions.AddRange(
            Expense(today.AddDays(-1), 1),
            Expense(today.AddDays(-2), 2, TransactionKind.Income),
            Expense(today.AddDays(-3), 2, TransactionKind.Income));
        await mem.Db.SaveChangesAsync();

        var r = await new CategoryService(mem.Db).GetFrequentAsync();

        Assert.Equal([1], r.Select(x => x.CategoryId));
    }

    private static Transaction Expense(
        DateOnly date, int categoryId, TransactionKind kind = TransactionKind.Expense) =>
        new()
        {
            Kind = kind, CurrencyOriginal = "PLN", AmountOriginal = 25m, AmountBase = 25m,
            FxRate = 1m, FxDate = date, Date = date, CategoryId = categoryId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task Rejects_duplicate_name()
    {
        using var mem = new SqliteInMemory();
        var sut = new CategoryService(mem.Db);

        var r = await sut.CreateAsync(new SaveCategoryRequest("Продукти", null, null));

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Conflict, r.Error.Type);
    }

    [Fact]
    public async Task Deleting_a_category_moves_its_transactions_to_the_fallback()
    {
        using var mem = new SqliteInMemory();
        mem.Db.Transactions.Add(new Transaction
        {
            CurrencyOriginal = "PLN", AmountOriginal = 50m, AmountBase = 50m, FxRate = 1m,
            FxDate = new DateOnly(2026, 7, 25), Date = new DateOnly(2026, 7, 25),
            CategoryId = 5, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        var r = await new CategoryService(mem.Db).DeleteAsync(5);

        Assert.True(r.IsSuccess);
        var tx = await mem.Db.Transactions.SingleAsync();
        Assert.Equal(FallbackId, tx.CategoryId); // money kept, just recategorized
        Assert.Equal(50m, tx.AmountBase);
    }

    [Fact]
    public async Task Deleting_a_category_moves_its_recurring_to_the_fallback()
    {
        using var mem = new SqliteInMemory();
        mem.Db.RecurringExpenses.Add(new RecurringExpense
        {
            AmountOriginal = 40m, CurrencyOriginal = "PLN", CategoryId = 5,
            StartsOn = new DateOnly(2026, 1, 10), Active = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        await new CategoryService(mem.Db).DeleteAsync(5);

        var rec = await mem.Db.RecurringExpenses.SingleAsync();
        Assert.Equal(FallbackId, rec.CategoryId);
    }

    [Fact]
    public async Task Cannot_delete_the_system_fallback_category()
    {
        using var mem = new SqliteInMemory();

        var r = await new CategoryService(mem.Db).DeleteAsync(FallbackId);

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Validation, r.Error.Type);
    }

    [Fact]
    public async Task Renames_and_keeps_transactions_attached()
    {
        using var mem = new SqliteInMemory();
        var sut = new CategoryService(mem.Db);

        var r = await sut.UpdateAsync(1, new SaveCategoryRequest("Продукти", "🥑", "#16a34a"));

        Assert.True(r.IsSuccess);
        Assert.Equal("Продукти", r.Value!.Name);
        Assert.Equal("🥑", r.Value.Icon);
    }
}
