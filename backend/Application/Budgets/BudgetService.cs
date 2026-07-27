using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Mapping;
using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Budgets;

public interface IBudgetService
{
    Task<BudgetResponse> GetAsync(CancellationToken ct = default);
    Task<BudgetResponse> SetAsync(decimal amount, CancellationToken ct = default);
}

public sealed class BudgetService(IAppDbContext db, IMoneyViewFactory moneyViews) : IBudgetService
{
    public async Task<BudgetResponse> GetAsync(CancellationToken ct = default)
    {
        var b = await db.Budgets.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return await ShowAsync(b, ct);
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
        return await ShowAsync(b, ct);
    }

    /// The stored budget is a PLN number about the month ahead — no date of its own, so it
    /// is read at today's rate.
    private async Task<BudgetResponse> ShowAsync(Budget? b, CancellationToken ct)
    {
        var view = await moneyViews.CurrentAsync(ct);
        return b is null
            ? new BudgetResponse(false, null, view.Currency, null)
            : new BudgetResponse(true, await view.FromBaseTodayAsync(b.MonthlyAmount, ct), view.Currency, b.UpdatedAt);
    }
}
