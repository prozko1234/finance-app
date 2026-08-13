using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceApp.Application.Budgets;

/// What was left when the last period ended, and where it should go.
/// <param name="Amount">Base currency, always positive — an overspent period is not offered
/// anywhere to be put.</param>
public record PendingCarryover(decimal Amount, DateOnly FromStart, DateOnly FromEnd, string EnvelopeName);

public interface ICarryoverService
{
    /// The question to put to the user, or null when there is nothing to ask: already
    /// answered for this period, nothing left over, or a period whose budget the app cannot
    /// work out in the first place.
    Task<PendingCarryover?> PendingAsync(CancellationToken ct = default);

    /// Records the answer, and carries it out. Answering is what stops the question coming
    /// back, so even "не рахувати" is written down.
    Task<Result<PeriodCarryover>> DecideAsync(
        CarryoverDecision decision, int? envelopeId, CancellationToken ct = default);
}

/// Money does not disappear at a period boundary, but until this existed the app behaved as
/// though it did: a new period's budget is the new income, so anything underspent showed up
/// nowhere but the bank balance. Over a few months that is the difference between an app you
/// trust with one number and one you quietly correct in your head.
///
/// Asked, not automatic. A leftover is sometimes a win to bank and sometimes the money for a
/// thing planned next week, and those are opposite instructions — the app cannot tell them
/// apart, and guessing wrong means either a false savings figure or money withdrawn from a jar
/// to undo a decision nobody made. One question, once a period, with the answer that is usually
/// right offered first.
public sealed class CarryoverService(
    IAppDbContext db, IBudgetPeriods periods, IMonthlyBudget monthlyBudget,
    ILogger<CarryoverService> log) : ICarryoverService
{
    public async Task<PendingCarryover?> PendingAsync(CancellationToken ct = default)
    {
        var current = await periods.CurrentAsync(ct);

        if (await db.PeriodCarryovers.AnyAsync(x => x.PeriodStart == current.Start, ct)) return null;

        // A counted opening balance already contains the leftover — it is the money in the
        // account. Asking where to put it again would move it twice.
        var thisPeriod = await monthlyBudget.ForAsync(current, DateOnly.FromDateTime(DateTime.Now), ct);
        if (thisPeriod.FromOpeningBalance) return null;

        var previous = await periods.ForAsync(current.Start.AddDays(-1), ct);
        var left = await LeftOverAsync(previous, ct);
        if (left is not { } amount || amount <= 0m) return null;

        var jar = await DefaultEnvelopeAsync(ct);
        if (jar is null) return null;

        return new PendingCarryover(amount, previous.Start, previous.End, jar.Name);
    }

    public async Task<Result<PeriodCarryover>> DecideAsync(
        CarryoverDecision decision, int? envelopeId, CancellationToken ct = default)
    {
        var pending = await PendingAsync(ct);
        if (pending is null)
            return Error.Validation("Немає залишку, про який питати — його вже розклали або його не було.");

        var current = await periods.CurrentAsync(ct);
        var row = new PeriodCarryover
        {
            PeriodStart = current.Start,
            AmountBase = pending.Amount,
            Decision = decision,
            DecidedAt = DateTimeOffset.UtcNow,
        };

        if (decision == CarryoverDecision.ToEnvelope)
        {
            var jar = envelopeId is { } id
                ? await db.Envelopes.FirstOrDefaultAsync(e => e.Id == id && e.ArchivedAt == null, ct)
                : await DefaultEnvelopeAsync(ct);
            if (jar is null) return Error.NotFound("Банку не знайдено.");

            row.EnvelopeId = jar.Id;

            // An ordinary hand-made deposit, not an IsAuto one: the scheme must be free to
            // re-pour its own entry without touching this, and taking the money back out is
            // the same withdrawal it would be for any other deposit.
            //
            // AlreadySetAside, though — and that is not a detail. This money came from the
            // PREVIOUS period; choosing the jar is precisely the answer that keeps it out of
            // this period's budget, so this period must not pay for it either. Without the
            // flag the deposit was held back from the daily norm like any other, and a
            // leftover of 800 quietly took 800 off what could be spent this month — money that
            // had never been in this month's income to begin with, charged for a second time.
            db.SavingsEntries.Add(new SavingsEntry
            {
                AlreadySetAside = true,
                EnvelopeId = jar.Id,
                Date = current.Start,
                Kind = SavingsEntryKind.Deposit,
                AmountOriginal = pending.Amount,
                CurrencyOriginal = Money.BaseCurrency,
                AmountBase = pending.Amount,
                FxRate = 1m,
                FxDate = current.Start,
                IsAuto = false,
                Note = "Залишок минулого періоду",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        db.PeriodCarryovers.Add(row);
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Carryover: {Amount} left over from {From:yyyy-MM-dd} went to {Decision}",
            pending.Amount, pending.FromStart, decision);

        return Result<PeriodCarryover>.Ok(row);
    }

    /// What a finished period had left: its budget, less what was spent out of it, less what
    /// went into jars. Deposits count as gone because the money left the spendable pile — it
    /// is already counted once as savings, and counting it again here would offer the user
    /// the same money twice.
    ///
    /// Null when the period had no budget the app could work out: an empty period has no
    /// leftover, it has no arithmetic at all.
    private async Task<decimal?> LeftOverAsync(BudgetPeriod period, CancellationToken ct)
    {
        var budget = await monthlyBudget.ForAsync(period, period.End, ct);
        if (budget.Budget is not { } total) return null;

        var from = budget.WindowStart;

        // Expenses paid out of a jar are left out for the same reason the home screen leaves
        // them out: that money stopped being spendable when it went into the jar.
        //
        // A recurring charge still waiting to be confirmed IS counted here, unlike on the home
        // screen. This period is over: a subscription whose day passed weeks ago has almost
        // certainly gone, and the one thing a leftover must never do is offer money that is
        // not in the account. Unconfirmed and old is treated as spent.
        var spent = await db.Transactions
            .Where(t => t.Kind == TransactionKind.Expense && t.EnvelopeId == null
                        && t.Date >= from && t.Date <= period.End)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var intoJars = await db.SavingsEntries
            .Where(x => x.Date >= from && x.Date <= period.End)
            .SumAsync(x => (decimal?)(x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase), ct)
            ?? 0m;

        return total - spent - intoJars;
    }

    private Task<Envelope?> DefaultEnvelopeAsync(CancellationToken ct) =>
        db.Envelopes.FirstOrDefaultAsync(e => e.IsDefault && e.ArchivedAt == null, ct);
}
