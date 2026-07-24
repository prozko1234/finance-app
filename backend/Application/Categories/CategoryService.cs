using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Categories;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken ct = default);
}

public sealed class CategoryService(IAppDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var cats = await db.Categories.OrderBy(c => c.Id).ToListAsync(ct);
        return cats.Select(c => c.ToResponse()).ToList();
    }
}
