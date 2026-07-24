using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Budgets;

public interface IBudgetService
{
    Task<BudgetResponse> GetAsync(CancellationToken ct = default);
    Task<BudgetResponse> SetAsync(decimal amount, CancellationToken ct = default);
}

public sealed class BudgetService(IAppDbContext db) : IBudgetService
{
    public async Task<BudgetResponse> GetAsync(CancellationToken ct = default)
    {
        var b = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return b.ToResponse();
    }

    public async Task<BudgetResponse> SetAsync(decimal amount, CancellationToken ct = default)
    {
        // MVP: a single active budget — upsert the first row.
        var b = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (b is null)
        {
            b = new Budget { MonthlyAmount = amount, UpdatedAt = DateTimeOffset.UtcNow };
            db.Budgets.Add(b);
        }
        else
        {
            b.MonthlyAmount = amount;
            b.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return b.ToResponse();
    }
}
