using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Recurring;

public interface IRecurringService
{
    Task<IReadOnlyList<RecurringResponse>> GetAllAsync(CancellationToken ct = default);
    Task<Result<RecurringResponse>> CreateAsync(SaveRecurringRequest req, CancellationToken ct = default);
    Task<Result<RecurringResponse>> UpdateAsync(int id, SaveRecurringRequest req, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class RecurringService(IAppDbContext db) : IRecurringService
{
    public async Task<IReadOnlyList<RecurringResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await db.RecurringExpenses
            .Include(r => r.Category)
            .OrderByDescending(r => r.Active).ThenBy(r => r.DayOfMonth)
            .ToListAsync(ct);
        return items.Select(r => r.ToResponse()).ToList();
    }

    public async Task<Result<RecurringResponse>> CreateAsync(SaveRecurringRequest req, CancellationToken ct = default)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            return Error.Validation($"Категорію {req.CategoryId} не знайдено.");

        var r = new RecurringExpense
        {
            AmountOriginal = req.Amount,
            CurrencyOriginal = req.Currency.ToUpperInvariant(),
            CategoryId = req.CategoryId,
            DayOfMonth = req.DayOfMonth,
            Active = req.Active,
            Note = req.Note,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.RecurringExpenses.Add(r);
        await db.SaveChangesAsync(ct);
        await LoadCategoryAsync(r, ct);
        return Result<RecurringResponse>.Ok(r.ToResponse());
    }

    public async Task<Result<RecurringResponse>> UpdateAsync(int id, SaveRecurringRequest req, CancellationToken ct = default)
    {
        var r = await db.RecurringExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Error.NotFound($"Підписку {id} не знайдено.");
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            return Error.Validation($"Категорію {req.CategoryId} не знайдено.");

        r.AmountOriginal = req.Amount;
        r.CurrencyOriginal = req.Currency.ToUpperInvariant();
        r.CategoryId = req.CategoryId;
        r.DayOfMonth = req.DayOfMonth;
        r.Active = req.Active;
        r.Note = req.Note;
        await db.SaveChangesAsync(ct);
        await LoadCategoryAsync(r, ct);
        return Result<RecurringResponse>.Ok(r.ToResponse());
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var r = await db.RecurringExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Error.NotFound($"Підписку {id} не знайдено.");

        // Already materialized transactions keep their history (FK set to null on delete).
        db.RecurringExpenses.Remove(r);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    private async Task LoadCategoryAsync(RecurringExpense r, CancellationToken ct) =>
        r.Category = await db.Categories.FirstOrDefaultAsync(c => c.Id == r.CategoryId, ct);
}
