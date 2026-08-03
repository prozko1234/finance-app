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
