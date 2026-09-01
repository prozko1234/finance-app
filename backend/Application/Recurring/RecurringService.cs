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

    /// «Оплачено ✓» for one charge the schedule wrote and nobody had confirmed yet.
    Task<Result<bool>> ConfirmChargeAsync(int transactionId, CancellationToken ct = default);

    /// Takes «Оплачено ✓» back. The tick is one tap on a card that appears unbidden at the top
    /// of the home screen, so it gets mis-tapped — and until now the only way out was to delete
    /// the charge, which says something else entirely: that it never happened.
    Task<Result<bool>> UnconfirmChargeAsync(int transactionId, CancellationToken ct = default);
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
        // Split by status: a charge the schedule wrote but nobody has ticked off has not «вже
        // пішло», and saying it has is how the screen came to disagree with the bank. It is
        // not silent either — an unanswered question on the row is the whole point.
        var written = await db.Transactions
            .Where(t => t.RecurringExpenseId != null && t.Date >= start && t.Date <= end)
            .Select(t => new { Id = t.RecurringExpenseId!.Value, t.Status, t.Date, TxId = t.Id })
            .ToListAsync(ct);

        var chargedIds = written.Where(t => t.Status == TxStatus.Posted).Select(t => t.Id).ToHashSet();
        var awaitingIds = written.Where(t => t.Status == TxStatus.Pending).Select(t => t.Id).ToHashSet();

        // The one charge a row's buttons operate on. A weekly charge can have several in a
        // period with different answers, so the unanswered one comes first — that is the
        // question worth asking — and only when there is none does the last confirmed one
        // stand in, which is what makes «Оплачено ✓» reversible after a mis-tap.
        var actionable = written
            .GroupBy(t => t.Id)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(t => t.Status == TxStatus.Pending ? 0 : 1)
                      .ThenBy(t => t.Status == TxStatus.Pending ? t.Date : DateOnly.MaxValue)
                      .ThenByDescending(t => t.Date)
                      .First());

        return items
            .Select(r => r.ToResponse() with
            {
                NextChargeOn = NextChargeOn(r),
                ChargedThisPeriod = chargedIds.Contains(r.Id),
                AwaitingConfirmation = awaitingIds.Contains(r.Id),
                ChargeId = actionable.TryGetValue(r.Id, out var c) ? c.TxId : null,
                ChargeOn = actionable.TryGetValue(r.Id, out var d) ? d.Date : null,
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
        // Parsed before anything is written: returning an error with the entity half-updated
        // leaves a dirty tracked row behind for whatever saves next.
        if (!TryParseUnit(req.Unit, out var unit))
            return Error.Validation($"Невідома періодичність: {req.Unit}.");

        var currency = req.Currency.ToUpperInvariant();

        // Whether the CHARGES this rule produces would come out differently. The pause switch
        // and the name are deliberately not in it: pausing means "no more of these", not "the
        // one that already fell due never happened", and dropping its unconfirmed charge would
        // quietly hand back money that has probably already gone.
        var chargesChanged =
            r.StartsOn != req.StartsOn || r.Unit != unit || r.Interval != req.Interval
            || r.AmountOriginal != req.Amount || r.CurrencyOriginal != currency
            || r.CategoryId != req.CategoryId;

        r.AmountIncludesVat = req.AmountIncludesVat;
        r.AmountOriginal = req.Amount;
        r.CurrencyOriginal = currency;
        r.CategoryId = req.CategoryId;
        r.StartsOn = req.StartsOn;
        r.Unit = unit;
        r.Interval = req.Interval;
        r.Active = req.Active;
        r.Note = req.Note;

        if (chargesChanged) await DropUnconfirmedChargesAsync(r.Id, ct);

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

    /// Charges this period that nobody has confirmed, thrown away so the next read can write
    /// them again from the rule as it now stands. A charge written on the 15th has nothing to
    /// attach to once the day moves to the 20th: it is no longer an occurrence, so nothing
    /// will ask about it and nothing will write it back — it would sit in the list forever as
    /// a question with no answer. The same goes for one written at the old price.
    ///
    /// Only this period, and only unconfirmed. Confirmed charges are history, and history does
    /// not move because a subscription's date did. A deleted occurrence leaves a
    /// <see cref="RecurringSkip"/> behind, which materialization still honours, so nothing
    /// the user threw away comes back through here.
    private async Task DropUnconfirmedChargesAsync(int recurringId, CancellationToken ct)
    {
        var (start, end) = await periods.CurrentAsync(ct);

        var unconfirmed = await db.Transactions
            .Where(t => t.RecurringExpenseId == recurringId && t.Status == TxStatus.Pending
                        && t.Date >= start && t.Date <= end)
            .ToListAsync(ct);

        db.Transactions.RemoveRange(unconfirmed);
    }

    /// Confirming twice is not an error: the second tap comes from a stale screen, and the
    /// state it asks for is the state the row is already in. Failing it would show the user a
    /// problem that does not exist.
    public async Task<Result<bool>> ConfirmChargeAsync(int transactionId, CancellationToken ct = default)
    {
        var tx = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.RecurringExpenseId != null, ct);
        if (tx is null) return Error.NotFound($"Списання {transactionId} не знайдено.");

        if (tx.Status == TxStatus.Pending)
        {
            tx.Status = TxStatus.Posted;
            await db.SaveChangesAsync(ct);
        }

        return Result<bool>.Ok(true);
    }

    /// The mirror of confirming, and forgiving in the same way: a charge already waiting is
    /// already in the state being asked for.
    public async Task<Result<bool>> UnconfirmChargeAsync(
        int transactionId, CancellationToken ct = default)
    {
        var tx = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.RecurringExpenseId != null, ct);
        if (tx is null) return Error.NotFound($"Списання {transactionId} не знайдено.");

        if (tx.Status == TxStatus.Posted)
        {
            tx.Status = TxStatus.Pending;
            await db.SaveChangesAsync(ct);
        }

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
