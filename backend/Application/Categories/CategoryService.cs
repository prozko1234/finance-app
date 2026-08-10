using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Auth;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Categories;

public interface ICategoryService
{
    /// <param name="kind">Null — both sides of the ledger, which is what the settings screen
    /// shows. The entry forms ask for one.</param>
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(
        CategoryKind? kind = null, CancellationToken ct = default);

    /// The categories to offer as one-tap shortcuts, most used first.
    Task<IReadOnlyList<FrequentCategoryResponse>> GetFrequentAsync(
        int limit = 4, CancellationToken ct = default);

    Task<Result<CategoryResponse>> CreateAsync(SaveCategoryRequest req, CancellationToken ct = default);
    Task<Result<CategoryResponse>> UpdateAsync(int id, SaveCategoryRequest req, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class CategoryService(IAppDbContext db, IUserProvisioning provisioning) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(
        CategoryKind? kind = null, CancellationToken ct = default)
    {
        if (kind == CategoryKind.Income) await provisioning.EnsureIncomeCategoriesAsync(ct);

        // Grouped by side of the ledger before anything else: asked for both, the two lists
        // would otherwise interleave by sort order and read as one muddled list where a salary
        // sits between groceries and rent.
        var cats = await db.Categories
            .Where(c => kind == null || c.Kind == kind)
            .OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return cats.Select(c => c.ToResponse()).ToList();
    }

    /// The windows the shortcuts are counted over: the fortnight the user is actually living
    /// in, widened to a month only when a fortnight is too thin to fill the row.
    private static readonly int[] FrequentWindows = [14, 30];

    /// <summary>
    /// The shortcuts are counted over a window of DAYS, deliberately — this used to be derived
    /// on the client from whatever page of recent transactions happened to be loaded, so the
    /// buttons reordered themselves when "показати ще" pulled in older rows, and a category
    /// abandoned months ago outranked one used every day this week. A fixed window makes the
    /// row both current and still: paging the list below can no longer move a button out from
    /// under the finger.
    ///
    /// Expenses only. Income is entered a few times a month through its own flow, and a
    /// shortcut for it would take a slot from something tapped daily.
    /// </summary>
    public async Task<IReadOnlyList<FrequentCategoryResponse>> GetFrequentAsync(
        int limit = 4, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        // The widest window is queried once and narrowed in memory: the rows are a handful
        // either way, and two round trips to answer one question is a round trip wasted.
        var widest = FrequentWindows[^1];
        var rows = await db.Transactions
            .Where(t => t.Kind == TransactionKind.Expense && t.Date > today.AddDays(-widest))
            .Select(t => new { t.Date, t.CategoryId, t.Category!.Name, t.Category.Icon })
            .ToListAsync(ct);

        foreach (var days in FrequentWindows)
        {
            var cutoff = today.AddDays(-days);
            var found = rows
                .Where(r => r.Date > cutoff)
                .GroupBy(r => (r.CategoryId, r.Name, r.Icon))
                // Ties broken by the most recent use, so the order is stable from one entry
                // to the next instead of flipping between equally-used categories.
                .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(r => r.Date))
                .Take(limit)
                .Select(g => new FrequentCategoryResponse(
                    g.Key.CategoryId, g.Key.Name, g.Key.Icon, g.Count(), days))
                .ToList();

            // A row of one or two buttons is not worth the space it takes, so a thin fortnight
            // falls through to the month. The last window is returned whatever it holds — an
            // empty list is a valid answer, and the screen simply drops the row.
            if (found.Count >= 3 || days == widest) return found;
        }

        return [];
    }

    public async Task<Result<CategoryResponse>> CreateAsync(SaveCategoryRequest req, CancellationToken ct = default)
    {
        var name = req.Name.Trim();
        if (!TryParseKind(req.Kind, out var kind))
            return Error.Validation($"Невідомий рід категорії: {req.Kind}.");

        // Uniqueness is per side of the ledger: "Фактура" as a source of money and as a thing
        // paid for are two different categories, and forbidding the second helps nobody.
        if (await db.Categories.AnyAsync(c => c.Name == name && c.Kind == kind, ct))
            return Error.Conflict($"Категорія «{name}» вже існує.");

        // New categories go to the end of their own list, but always before its fallback.
        var maxOrder = await db.Categories.Where(c => !c.IsSystem && c.Kind == kind)
            .Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;

        var c = new Category
        {
            Name = name,
            Kind = kind,
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
        if (await db.Categories.AnyAsync(x => x.Name == name && x.Kind == c.Kind && x.Id != id, ct))
            return Error.Conflict($"Категорія «{name}» вже існує.");

        c.Name = name;
        c.Icon = req.Icon;
        c.Color = req.Color;
        await db.SaveChangesAsync(ct);
        return Result<CategoryResponse>.Ok(c.ToResponse());
    }

    /// Deleting a category never deletes money: its transactions and recurring expenses
    /// are moved to the system fallback category first.
    /// Missing kind means expense — that is what every category created before income had its
    /// own is, and what the plain "+ Нова" button beside an expense form sends.
    private static bool TryParseKind(string? kind, out CategoryKind parsed)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            parsed = CategoryKind.Expense;
            return true;
        }
        return Enum.TryParse(kind, ignoreCase: true, out parsed);
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Error.NotFound($"Категорію {id} не знайдено.");
        if (c.IsSystem)
            return Error.Validation("Категорію «Інше» видалити не можна — у неї переносяться інші.");

        // The fallback of the SAME side of the ledger: moving a salary into the spending
        // "Інше" would file income in a list that only ever sums what went out.
        var fallback = await db.Categories.FirstOrDefaultAsync(x => x.IsSystem && x.Kind == c.Kind, ct);
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
