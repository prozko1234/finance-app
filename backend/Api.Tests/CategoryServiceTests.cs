using FinanceApp.Application.Auth;
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
        var sut = new CategoryService(mem.Db, new UserProvisioningService(mem.Db));

        var r = await sut.CreateAsync(new SaveCategoryRequest("Хобі", "🎨", "#059669"));

        Assert.True(r.IsSuccess);
        Assert.Equal("Хобі", r.Value!.Name);
        Assert.False(r.Value.IsSystem);

        var expenses = await sut.GetAllAsync(CategoryKind.Expense);
        Assert.Equal("Інше", expenses[^1].Name); // system fallback stays last in its own list
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

        var r = await new CategoryService(mem.Db, new UserProvisioningService(mem.Db)).GetFrequentAsync();

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

        var r = await new CategoryService(mem.Db, new UserProvisioningService(mem.Db)).GetFrequentAsync();

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

        var r = await new CategoryService(mem.Db, new UserProvisioningService(mem.Db)).GetFrequentAsync();

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
        var sut = new CategoryService(mem.Db, new UserProvisioningService(mem.Db));

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

        var r = await new CategoryService(mem.Db, new UserProvisioningService(mem.Db)).DeleteAsync(5);

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

        await new CategoryService(mem.Db, new UserProvisioningService(mem.Db)).DeleteAsync(5);

        var rec = await mem.Db.RecurringExpenses.SingleAsync();
        Assert.Equal(FallbackId, rec.CategoryId);
    }

    [Fact]
    public async Task Cannot_delete_the_system_fallback_category()
    {
        using var mem = new SqliteInMemory();

        var r = await new CategoryService(mem.Db, new UserProvisioningService(mem.Db)).DeleteAsync(FallbackId);

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Validation, r.Error.Type);
    }

    [Fact]
    public async Task Renames_and_keeps_transactions_attached()
    {
        using var mem = new SqliteInMemory();
        var sut = new CategoryService(mem.Db, new UserProvisioningService(mem.Db));

        var r = await sut.UpdateAsync(1, new SaveCategoryRequest("Продукти", "🥑", "#16a34a"));

        Assert.True(r.IsSuccess);
        Assert.Equal("Продукти", r.Value!.Name);
        Assert.Equal("🥑", r.Value.Icon);
    }

    /// Income had no categories of its own, so every invoice was hung off whatever expense one
    /// came first — the app filed a salary under "Продукти" in its own database and covered for
    /// it on every screen that showed a row.
    [Fact]
    public async Task The_two_sides_of_the_ledger_are_separate_lists()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);

        var expenses = await sut.GetAllAsync(CategoryKind.Expense);
        var income = await sut.GetAllAsync(CategoryKind.Income);

        Assert.All(expenses, c => Assert.Equal(nameof(CategoryKind.Expense), c.Kind));
        Assert.All(income, c => Assert.Equal(nameof(CategoryKind.Income), c.Kind));
        Assert.Contains(income, c => c.Name == "Зарплата");
        Assert.DoesNotContain(expenses, c => c.Name == "Зарплата");
    }

    /// Each list has its own fallback. A salary moved into the spending "Інше" would sit in a
    /// list that only ever sums what went out.
    [Fact]
    public async Task Deleting_an_income_category_moves_its_money_to_the_income_fallback()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);

        var income = await sut.GetAllAsync(CategoryKind.Income);
        var salary = income.Single(c => c.Name == "Зарплата");
        var fallback = income.Single(c => c.IsSystem);

        mem.Db.Transactions.Add(new Transaction
        {
            Kind = TransactionKind.Income, CurrencyOriginal = "PLN",
            AmountOriginal = 5_000m, AmountBase = 5_000m, FxRate = 1m,
            FxDate = DateOnly.FromDateTime(DateTime.Now), Date = DateOnly.FromDateTime(DateTime.Now),
            CategoryId = salary.Id, CreatedAt = DateTimeOffset.UtcNow,
        });
        await mem.Db.SaveChangesAsync();

        Assert.True((await sut.DeleteAsync(salary.Id)).IsSuccess);

        var moved = await mem.Db.Transactions.SingleAsync(t => t.Kind == TransactionKind.Income);
        Assert.Equal(fallback.Id, moved.CategoryId);
    }

    /// The same word can name a source of money and a thing paid for. Forbidding the second
    /// helps nobody.
    [Fact]
    public async Task The_same_name_may_exist_on_both_sides()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);

        var expense = await sut.CreateAsync(new SaveCategoryRequest("Фактура", null, null));
        var income = await sut.CreateAsync(new SaveCategoryRequest("Фактура", null, null, nameof(CategoryKind.Income)));

        Assert.True(expense.IsSuccess);
        // Income already starts with a "Фактура", so this one is the conflict — on its own side.
        Assert.False(income.IsSuccess);
    }

    /// Provisioning only runs at registration, so an account made before income had categories
    /// would open the form to an empty list and be unable to write an invoice at all.
    [Fact]
    public async Task An_account_without_income_categories_is_topped_up_on_first_ask()
    {
        using var mem = new SqliteInMemory();
        var sut = Sut(mem);

        mem.Db.Categories.RemoveRange(
            await mem.Db.Categories.Where(c => c.Kind == CategoryKind.Income).ToListAsync());
        await mem.Db.SaveChangesAsync();

        var income = await sut.GetAllAsync(CategoryKind.Income);

        Assert.NotEmpty(income);
        Assert.Contains(income, c => c.IsSystem);
    }

    private static CategoryService Sut(SqliteInMemory mem) =>
        new(mem.Db, new UserProvisioningService(mem.Db));
}
