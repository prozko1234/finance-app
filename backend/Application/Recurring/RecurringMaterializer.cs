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

    /// How far back a single catch-up may reach. Two years covers any realistic gap between
    /// app loads while keeping the work bounded no matter what a date field says.
    private const int MaxCatchUpMonths = 24;

    public async Task MaterializeDueAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var recurring = await db.RecurringExpenses.Where(r => r.Active).ToListAsync(ct);

            foreach (var r in recurring)
            {
                // A row with no CreatedAt (default = year 1) would make the walk below write a
                // charge for every month since antiquity — ~24 000 phantom transactions, which
                // is exactly what a dev seed missing the field once did. A subscription cannot
                // have been charged before it existed, so an unset date means "starts now".
                var createdDate = r.CreatedAt == default
                    ? today
                    : DateOnly.FromDateTime(r.CreatedAt.ToLocalTime().DateTime);

                var currentMonth = new DateOnly(today.Year, today.Month, 1);
                var month = new DateOnly(createdDate.Year, createdDate.Month, 1);

                // Backstop for any other way a bad date could get in: lazy materialization is
                // meant to catch up days or weeks of missed loads, never centuries.
                var earliest = currentMonth.AddMonths(-MaxCatchUpMonths);
                if (month < earliest) month = earliest;

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

        var tx = new Transaction
        {
            Kind = r.Kind,
            AmountOriginal = r.AmountOriginal,
            CurrencyOriginal = r.CurrencyOriginal,
            AmountBase = conv.Value!.AmountBase,
            FxRate = conv.Value.Rate,
            FxDate = conv.Value.RateDate,
            CategoryId = r.CategoryId,
            RecurringExpenseId = r.Id,
            Frequency = Frequency.Recurring,
            Source = TxSource.Recurring,
            Date = occ,
            Note = r.Note,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // A recurring salary is income like any other: VAT is split out and AmountBase holds
        // the revenue, exactly as the manual income form does. Anything else and the month's
        // taxes would be computed on a number that includes VAT.
        if (r.Kind == TransactionKind.Income)
        {
            var profile = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            var vatRate = profile is { VatPayer: true } ? profile.VatRate : 0m;
            var entered = conv.Value!.AmountBase;

            var revenue = r.AmountIncludesVat
                ? Math.Round(entered / (1 + vatRate), 2, MidpointRounding.AwayFromZero)
                : entered;
            var gross = r.AmountIncludesVat
                ? entered
                : Math.Round(entered * (1 + vatRate), 2, MidpointRounding.AwayFromZero);

            tx.AmountBase = revenue;
            tx.GrossWithVat = gross;
            tx.VatAmount = Math.Round(gross - revenue, 2, MidpointRounding.AwayFromZero);
        }

        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);
    }
}
