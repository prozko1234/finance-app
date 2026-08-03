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
///
/// Which dates are due is not decided here: <see cref="RecurringSchedule"/> owns that, so
/// weekly, fortnightly, quarterly and yearly rules all arrive as a plain list of dates.
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

            // Backstop against a bad date: lazy materialization is meant to catch up days or
            // weeks of missed loads, never centuries. A row whose StartsOn was never set
            // (default = year 1) would otherwise write a charge for every week since
            // antiquity — which is exactly what a dev seed missing the field once did.
            var earliest = today.AddMonths(-MaxCatchUpMonths);

            // Occurrences the user has deleted. Loaded once for the whole run: the set is
            // tiny, and asking per date would be a query per charge per load.
            var skipped = (await db.RecurringSkips
                    .Where(s => s.Date >= earliest)
                    .Select(s => new { s.RecurringExpenseId, s.Date })
                    .ToListAsync(ct))
                .Select(s => (s.RecurringExpenseId, s.Date))
                .ToHashSet();

            foreach (var r in recurring)
            {
                var from = r.StartsOn < earliest ? earliest : r.StartsOn;

                foreach (var occ in RecurringSchedule.Occurrences(r.StartsOn, r.Unit, r.Interval, from, today))
                {
                    if (skipped.Contains((r.Id, occ))) continue;
                    await MaterializeOneAsync(r, occ, ct);
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
