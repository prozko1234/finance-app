using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Fx;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Recurring;

public interface IRecurringMaterializer
{
    /// Create the Transactions for any recurring charges that fell due since last run.
    Task MaterializeDueAsync(CancellationToken ct = default);
}

/// Lazy generation: called when data is read (home load). No background job.
/// Idempotent — one transaction per recurring per due date, guarded by a unique index
/// and serialized by a process-wide gate (single local instance).
public sealed class RecurringMaterializer(IAppDbContext db, IFxConverter fx) : IRecurringMaterializer
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task MaterializeDueAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var recurring = await db.RecurringExpenses.Where(r => r.Active).ToListAsync(ct);

            foreach (var r in recurring)
            {
                var createdDate = DateOnly.FromDateTime(r.CreatedAt.ToLocalTime().DateTime);
                var month = new DateOnly(createdDate.Year, createdDate.Month, 1);
                var currentMonth = new DateOnly(today.Year, today.Month, 1);

                while (month <= currentMonth)
                {
                    var occ = RecurringSchedule.OccurrenceDate(month.Year, month.Month, r.DayOfMonth);
                    if (occ <= today && occ >= createdDate)
                        await MaterializeOneAsync(r, occ, ct);
                    month = month.AddMonths(1);
                }
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task MaterializeOneAsync(RecurringExpense r, DateOnly occ, CancellationToken ct)
    {
        var exists = await db.Transactions.AnyAsync(t => t.RecurringExpenseId == r.Id && t.Date == occ, ct);
        if (exists) return;

        // Skip if the rate is unavailable for this currency/date — retried on the next load.
        var conv = await fx.ConvertToBaseAsync(r.AmountOriginal, r.CurrencyOriginal, occ, ct);
        if (!conv.IsSuccess) return;

        db.Transactions.Add(new Transaction
        {
            AmountOriginal = r.AmountOriginal,
            CurrencyOriginal = r.CurrencyOriginal,
            AmountBase = conv.Value!.AmountBase,
            FxRate = conv.Value.Rate,
            FxDate = conv.Value.RateDate,
            CategoryId = r.CategoryId,
            RecurringExpenseId = r.Id,
            Priority = Priority.Must,
            Frequency = Frequency.Recurring,
            Source = TxSource.Recurring,
            Date = occ,
            Note = r.Note,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
