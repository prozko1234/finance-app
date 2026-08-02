using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
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

public sealed class RecurringService(IAppDbContext db, IBudgetPeriods periods) : IRecurringService
{
    public async Task<IReadOnlyList<RecurringResponse>> GetAllAsync(CancellationToken ct = default)
    {
        // Ordered after loading, not in SQL: SQLite sorts a DateOnly as text, and relying on
        // that is the same trap that a DateTimeOffset ORDER BY already sprang once.
        var items = (await db.RecurringExpenses
                .Include(r => r.Category)
                .ToListAsync(ct))
            .OrderByDescending(r => r.Active)
            .ThenBy(r => r.StartsOn)
            .ToList();

        // Which of these have already been taken out of the period being lived in. Asked once
        // for the whole list rather than per row: the answer is a set of ids, not a query each.
        var (start, end) = await periods.CurrentAsync(ct);
        var charged = await db.Transactions
            .Where(t => t.RecurringExpenseId != null && t.Date >= start && t.Date <= end)
            .Select(t => t.RecurringExpenseId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var chargedIds = charged.ToHashSet();

        return items
            .Select(r => r.ToResponse() with
            {
                NextChargeOn = NextChargeOn(r),
                ChargedThisPeriod = chargedIds.Contains(r.Id),
            })
            .ToList();
    }

    public async Task<Result<RecurringResponse>> CreateAsync(SaveRecurringRequest req, CancellationToken ct = default)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            return Error.Validation($"Категорію {req.CategoryId} не знайдено.");

        if (!TryParseKind(req.Kind, out var kind))
            return Error.Validation($"Невідомий тип: {req.Kind}.");

        if (!TryParseUnit(req.Unit, out var unit))
            return Error.Validation($"Невідома періодичність: {req.Unit}.");

        var r = new RecurringExpense
        {
            Kind = kind,
            AmountIncludesVat = req.AmountIncludesVat,
            AmountOriginal = req.Amount,
            CurrencyOriginal = req.Currency.ToUpperInvariant(),
            CategoryId = req.CategoryId,
            StartsOn = req.StartsOn,
            Unit = unit,
            Interval = req.Interval,
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

        // An update that does not say what kind this is keeps the kind it already had. The
        // default — Expense — belongs to creation only: applied here it would turn a paused
        // salary into a subscription the moment someone tapped ⏸, and the month would lose an
        // income and gain a charge without a word about it.
        if (req.Kind is not null)
        {
            if (!TryParseKind(req.Kind, out var kind))
                return Error.Validation($"Невідомий тип: {req.Kind}.");
            r.Kind = kind;
        }
        r.AmountIncludesVat = req.AmountIncludesVat;
        r.AmountOriginal = req.Amount;
        r.CurrencyOriginal = req.Currency.ToUpperInvariant();
        if (!TryParseUnit(req.Unit, out var unit))
            return Error.Validation($"Невідома періодичність: {req.Unit}.");

        r.CategoryId = req.CategoryId;
        r.StartsOn = req.StartsOn;
        r.Unit = unit;
        r.Interval = req.Interval;
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

    /// Missing kind means expense — that is what every row created before recurring
    /// income existed is, and what the plain "+ Підписка" flow sends.
    private static bool TryParseKind(string? kind, out TransactionKind parsed)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            parsed = TransactionKind.Expense;
            return true;
        }
        return Enum.TryParse(kind, ignoreCase: true, out parsed);
    }

    /// Missing periodicity means monthly — which is what every row created before weekly and
    /// yearly schedules existed is.
    private static bool TryParseUnit(string? unit, out RecurrenceUnit parsed)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            parsed = RecurrenceUnit.Month;
            return true;
        }
        return Enum.TryParse(unit, ignoreCase: true, out parsed);
    }

    /// The next day this falls due. A charge due today has already been written by the time
    /// anything is read (materialization runs on read), so today counts as gone.
    private static DateOnly? NextChargeOn(RecurringExpense r) =>
        r.Active
            ? RecurringSchedule.NextAfter(r.StartsOn, r.Unit, r.Interval, DateOnly.FromDateTime(DateTime.Now))
            : null;

    private async Task LoadCategoryAsync(RecurringExpense r, CancellationToken ct) =>
        r.Category = await db.Categories.FirstOrDefaultAsync(c => c.Id == r.CategoryId, ct);
}
