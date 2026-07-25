using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Categories;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken ct = default);
    Task<Result<CategoryResponse>> CreateAsync(SaveCategoryRequest req, CancellationToken ct = default);
    Task<Result<CategoryResponse>> UpdateAsync(int id, SaveCategoryRequest req, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class CategoryService(IAppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var cats = await db.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return cats.Select(c => c.ToResponse()).ToList();
    }

    public async Task<Result<CategoryResponse>> CreateAsync(SaveCategoryRequest req, CancellationToken ct = default)
    {
        var name = req.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.Name == name, ct))
            return Error.Conflict($"Категорія «{name}» вже існує.");

        // New categories go to the end, but always before the system fallback.
        var maxOrder = await db.Categories.Where(c => !c.IsSystem)
            .Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;

        var c = new Category
        {
            Name = name,
            Icon = req.Icon,
            Color = req.Color,
            SortOrder = maxOrder + 1,
        };
        db.Categories.Add(c);
        await db.SaveChangesAsync(ct);
        return Result<CategoryResponse>.Ok(c.ToResponse());
    }

    public async Task<Result<CategoryResponse>> UpdateAsync(int id, SaveCategoryRequest req, CancellationToken ct = default)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound($"Категорію {id} не знайдено.");

        var name = req.Name.Trim();
        if (await db.Categories.AnyAsync(x => x.Name == name && x.Id != id, ct))
            return Error.Conflict($"Категорія «{name}» вже існує.");

        c.Name = name;
        c.Icon = req.Icon;
        c.Color = req.Color;
        await db.SaveChangesAsync(ct);
        return Result<CategoryResponse>.Ok(c.ToResponse());
    }

    /// Deleting a category never deletes money: its transactions and recurring expenses
    /// are moved to the system fallback category first.
    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound($"Категорію {id} не знайдено.");
        if (c.IsSystem)
            return Error.Validation("Категорію «Інше» видалити не можна — у неї переносяться інші.");

        var fallback = await db.Categories.FirstOrDefaultAsync(x => x.IsSystem, ct);
        if (fallback is null)
            return Error.Conflict("Немає системної категорії для перенесення.");

        var moved = await db.Transactions.Where(t => t.CategoryId == id).ToListAsync(ct);
        foreach (var t in moved) t.CategoryId = fallback.Id;

        var movedRecurring = await db.RecurringExpenses.Where(r => r.CategoryId == id).ToListAsync(ct);
        foreach (var r in movedRecurring) r.CategoryId = fallback.Id;

        db.Categories.Remove(c);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }
}
