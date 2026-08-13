using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Debts;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Debts;

/// One movement out of a jar because of a debt — money lent out of it, or a repayment made
/// from it — in the shape the envelope arithmetic already handles expenses paid from a jar.
public record EnvelopeDebtPayment(int EnvelopeId, DateOnly Date, decimal AmountBase);

/// Everything the budget arithmetic needs to know about debts, and nothing else.
///
/// Debt payments are deliberately NOT transactions, so no existing sum picks them up by
/// itself. That keeps categories and the statistics honest — «повернув Сергію» is not a
/// spending category, and money coming back is not income to be taxed — but it means each
/// figure a payment belongs in has to ask for it. This is the one place that knows how, so
/// the summary, the monthly budget and the jars cannot end up with three different answers.
public interface IDebtLedger
{
    /// Money that LEFT spendable in the window because of a debt: a repayment made out of
    /// ordinary money, and money lent out of it. Both are real spending, and the daily norm
    /// has to feel them like any other.
    ///
    /// Lending belongs here for the reason it was missing: without it, handing somebody 500 zł
    /// changed nothing on screen, and then the 500 coming back was added to the budget — so
    /// the app printed money on every loan that was repaid.
    Task<decimal> OutOfSpendableAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// Money that ARRIVED into spendable in the window because of a debt: money paid back to
    /// the user, and money borrowed from somebody.
    ///
    /// This is not income. Running it through the tax engine would charge VAT, ZUS and PIT on
    /// money that was already the user's before they lent it out — or on money that is not
    /// theirs at all and has to be given back — and inflate the budget it is added to. It
    /// joins the budget after tax, exactly where a carried-over leftover does.
    /// <param name="recordedAfter">When set, money dated on <paramref name="from"/> only
    /// counts if it was entered after this moment — the tie-break for the day a balance was
    /// counted, matching how income is treated on that day.</param>
    Task<decimal> IntoSpendableAsync(
        DateOnly from, DateOnly to, DateTimeOffset? recordedAfter, CancellationToken ct = default);

    /// What debts hold back from this period's norm: only debts the user asked to reserve
    /// for, and only the part not already paid this period.
    Task<decimal> ReservedAsync(BudgetPeriod period, CancellationToken ct = default);

    /// The same figure per debt, keyed by id. The debts screen shows it beside each debt and
    /// the home screen sums it; they read the same number from here rather than each working
    /// out its own, because two ways of saying «скільки це в мене забирає» that disagree is
    /// exactly the confusion this feature was built to end.
    Task<IReadOnlyDictionary<int, decimal>> ReserveByDebtAsync(
        BudgetPeriod period, CancellationToken ct = default);

    /// Everything debts have taken straight out of a jar: repayments made from one, and money
    /// lent out of one. They leave the jar the same way a withdrawal does — without this the
    /// pot would keep showing money that is in somebody else's hands.
    Task<IReadOnlyList<EnvelopeDebtPayment>> FromEnvelopesAsync(CancellationToken ct = default);
}

public sealed class DebtLedger(IAppDbContext db, IBudgetPeriods periods) : IDebtLedger
{
    public async Task<decimal> OutOfSpendableAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var repaid = await db.DebtPayments
            .Where(p => p.Source == MoneySource.Spendable
                        && p.Debt!.Direction == DebtDirection.IOwe
                        && p.Date >= from && p.Date <= to)
            .SumAsync(p => (decimal?)p.AmountBase, ct) ?? 0m;

        // Money handed to somebody else is gone from the account exactly like a purchase is.
        var lent = await db.Debts
            .Where(d => d.Origin == MoneySource.Spendable
                        && d.Direction == DebtDirection.TheyOweMe
                        && d.Date >= from && d.Date <= to)
            .SumAsync(d => (decimal?)d.AmountBase, ct) ?? 0m;

        return repaid + lent;
    }

    public async Task<decimal> IntoSpendableAsync(
        DateOnly from, DateOnly to, DateTimeOffset? recordedAfter, CancellationToken ct = default)
    {
        var back = await db.DebtPayments
            .Where(p => p.Source == MoneySource.Spendable
                        && p.Debt!.Direction == DebtDirection.TheyOweMe
                        && p.Date >= from && p.Date <= to)
            .Select(p => new { p.Date, p.AmountBase, p.CreatedAt })
            .ToListAsync(ct);

        // Borrowed money is in the account and can be spent, so the norm has to know about it.
        // It is not a windfall — the whole sum is owed back, and reserving for that is what the
        // debt's own «відкладати щоперіоду» switch is for.
        var borrowed = await db.Debts
            .Where(d => d.Origin == MoneySource.Spendable
                        && d.Direction == DebtDirection.IOwe
                        && d.Date >= from && d.Date <= to)
            .Select(d => new { d.Date, d.AmountBase, d.CreatedAt })
            .ToListAsync(ct);

        // Compared in memory rather than in SQL, like the income the same rule applies to:
        // SQLite has no real DateTimeOffset, and it only ever affects one day's rows.
        return back.Concat(borrowed)
            .Where(p => recordedAfter is not { } cutoff || p.Date > from || p.CreatedAt > cutoff)
            .Sum(p => p.AmountBase);
    }

    public async Task<decimal> ReservedAsync(BudgetPeriod period, CancellationToken ct = default) =>
        (await ReserveByDebtAsync(period, ct)).Values.Sum();

    public async Task<IReadOnlyDictionary<int, decimal>> ReserveByDebtAsync(
        BudgetPeriod period, CancellationToken ct = default)
    {
        var debts = await db.Debts
            .Where(d => d.ReserveFromBudget
                        && d.Direction == DebtDirection.IOwe
                        && d.ClosedOn == null
                        && d.Deadline != null)
            .Select(d => new { d.Id, d.AmountBase, d.Deadline })
            .ToListAsync(ct);

        if (debts.Count == 0) return new Dictionary<int, decimal>();

        var ids = debts.Select(d => d.Id).ToList();
        var payments = await db.DebtPayments
            .Where(p => ids.Contains(p.DebtId))
            .Select(p => new { p.DebtId, p.Date, p.AmountBase })
            .ToListAsync(ct);

        var reserved = new Dictionary<int, decimal>(debts.Count);

        foreach (var debt in debts)
        {
            var mine = payments.Where(p => p.DebtId == debt.Id).ToList();
            var thisPeriod = mine
                .Where(p => p.Date >= period.Start && p.Date <= period.End)
                .Sum(p => p.AmountBase);

            // The pace is worked out on what was still owed when the period BEGAN, not on
            // what is owed now. Recomputing it mid-period would shrink this period's duty
            // every time it was met — pay half of it and the app asks for half of the half,
            // so the reserve and the payment together never add up to the plan.
            var owedAtStart = debt.AmountBase - (mine.Sum(p => p.AmountBase) - thisPeriod);
            var periodsLeft = await periods.CountUntilAsync(debt.Deadline, period, ct);
            var duty = EnvelopeTargets.Pace(owedAtStart, 0m, periodsLeft).PerPeriod;

            // What has already been paid this period counts towards the duty, whichever
            // pocket it came from. Otherwise a repayment made out of spendable money would be
            // charged to the norm twice: once as the payment, once as the reserve it satisfied.
            reserved[debt.Id] = Math.Max(0m, duty - thisPeriod);
        }

        return reserved;
    }

    public async Task<IReadOnlyList<EnvelopeDebtPayment>> FromEnvelopesAsync(
        CancellationToken ct = default)
    {
        var repaid = await db.DebtPayments
            .Where(p => p.Source == MoneySource.Envelope && p.EnvelopeId != null)
            .Select(p => new EnvelopeDebtPayment(p.EnvelopeId!.Value, p.Date, p.AmountBase))
            .ToListAsync(ct);

        var lent = await db.Debts
            .Where(d => d.Origin == MoneySource.Envelope && d.OriginEnvelopeId != null)
            .Select(d => new EnvelopeDebtPayment(d.OriginEnvelopeId!.Value, d.Date, d.AmountBase))
            .ToListAsync(ct);

        return [.. repaid, .. lent];
    }
}
