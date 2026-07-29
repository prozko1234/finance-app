using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Common;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Envelopes;

/// One envelope, this month. Same four numbers the savings pot always had — envelopes are
/// that idea applied to every bucket that is not spending money.
/// <param name="StillToReserve">Goal not yet moved by hand. This is what hides from
/// safe-to-spend; a deposit already made hides through <see cref="DepositedThisMonth"/>,
/// so the same money is never held back twice.</param>
public record EnvelopeStatus(
    int Id,
    string Name,
    BucketKind Kind,
    bool IsDefault,
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve)
{
    /// What this envelope takes out of "скільки можна витратити" this month.
    public decimal HeldBack => DepositedThisMonth + StillToReserve;
}

public interface IEnvelopeService
{
    /// Every envelope with its balance and this month's goal, in scheme order.
    /// <param name="countedOn">The day an opening balance was taken, when the month started
    /// mid-way. That figure is what is left to LIVE on: whatever was meant to be put aside
    /// either already was — and is therefore already outside the counted money — or is out
    /// of reach this month. So goals stand down, and only deposits made SINCE the count are
    /// held back. Reserving percentages of the remainder again would drop the daily norm to
    /// almost nothing, which is the exact problem the opening balance exists to fix.</param>
    Task<IReadOnlyList<EnvelopeStatus>> StatusAsync(
        decimal monthlyBudget, DateOnly? countedOn = null, CancellationToken ct = default);
}

public sealed class EnvelopeService(
    IAppDbContext db, IAllocationService allocations, IBudgetPeriods periods) : IEnvelopeService
{
    public async Task<IReadOnlyList<EnvelopeStatus>> StatusAsync(
        decimal monthlyBudget, DateOnly? countedOn = null, CancellationToken ct = default)
    {
        var scheme = await allocations.GetActiveAsync(ct);
        var goals = countedOn is null
            ? await GoalsAsync(scheme, monthlyBudget, ct)
            : [];
        var envelopes = await SyncAsync(scheme, ct);
        var period = await periods.CurrentAsync(ct);
        await FillAsync(scheme, envelopes, goals, period, ct);

        var balances = await db.SavingsEntries
            .GroupBy(x => x.EnvelopeId)
            .Select(g => new
            {
                EnvelopeId = g.Key,
                Balance = g.Sum(x => x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase),
            })
            .ToDictionaryAsync(x => x.EnvelopeId, x => x.Balance, ct);

        var (first, last) = period;
        var from = countedOn ?? first;
        var thisMonth = await db.SavingsEntries
            .Where(x => x.Date >= from && x.Date <= last)
            .GroupBy(x => x.EnvelopeId)
            .Select(g => new
            {
                EnvelopeId = g.Key,
                Net = g.Sum(x => x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase),
            })
            .ToDictionaryAsync(x => x.EnvelopeId, x => x.Net, ct);

        // Money spent straight out of an envelope leaves it the same way a withdrawal does.
        // Without this the pot would keep showing money that has already been paid to a shop
        // — and the expense, which the summary excludes precisely because the envelope
        // already holds it back, would vanish from the app's arithmetic entirely.
        var spentFrom = await db.Transactions
            .Where(t => t.EnvelopeId != null && t.Kind == TransactionKind.Expense)
            .Select(t => new { EnvelopeId = t.EnvelopeId!.Value, t.Date, t.AmountBase })
            .ToListAsync(ct);

        foreach (var group in spentFrom.GroupBy(t => t.EnvelopeId))
        {
            balances[group.Key] = balances.GetValueOrDefault(group.Key) - group.Sum(t => t.AmountBase);
            thisMonth[group.Key] = thisMonth.GetValueOrDefault(group.Key)
                - group.Where(t => t.Date >= from && t.Date <= last).Sum(t => t.AmountBase);
        }

        // Bucket order first, then whatever is left over from an older scheme: an envelope
        // whose bucket is gone keeps its balance but no longer reserves anything.
        var order = scheme.Buckets
            .OrderBy(b => b.SortOrder)
            .Select((b, i) => (b.Name, Index: i))
            .ToDictionary(x => x.Name, x => x.Index);

        return envelopes
            .OrderBy(e => order.TryGetValue(e.Name, out var i) ? i : int.MaxValue)
            .ThenBy(e => e.Id)
            .Select(e =>
            {
                var goal = goals.GetValueOrDefault(e.Name);
                var deposited = thisMonth.GetValueOrDefault(e.Id);
                var status = SavingsCalculator.Status(goal, balances.GetValueOrDefault(e.Id), deposited);
                return new EnvelopeStatus(
                    e.Id, e.Name, e.Kind, e.IsDefault,
                    status.Balance, status.MonthGoal, status.DepositedThisMonth, status.StillToReserve);
            })
            .ToList();
    }

    /// Carries out the scheme instead of asking the user to. Choosing «20% у заощадження»
    /// used to mean only that 20% was subtracted from the daily norm — the pot itself stayed
    /// empty until money was moved into it by hand, every single month. That is exactly the
    /// kind of standing chore this app exists to remove, so the app moves it.
    ///
    /// One entry per envelope per period, kept in step with the goal rather than topped up:
    /// a second invoice raises the budget, and a trail of correcting deposits would make the
    /// envelope's history unreadable.
    ///
    /// A deposit made BY HAND is now extra, on top of the plan — it used to be the only way
    /// to meet the goal, so it counted towards it. Now the app meets the goal itself, and
    /// someone who still moves money in means "more than planned". Withdrawals are left
    /// alone: taking money out is a decision, and refilling it on the next page load would
    /// silently overrule it.
    ///
    /// No goals at all means the plan has stood down — the user started mid-period by
    /// counting what they have (see the countedOn parameter on StatusAsync). Then what the
    /// app had already set aside on paper is withdrawn too: the counted figure is money the
    /// user says is theirs to live on, and holding some of it back again would take the
    /// daily norm apart from underneath.
    private async Task FillAsync(
        AllocationScheme scheme, List<Envelope> envelopes, Dictionary<string, decimal> goals,
        BudgetPeriod period, CancellationToken ct)
    {
        var entries = await db.SavingsEntries
            .Where(x => x.Date >= period.Start && x.Date <= period.End)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var changed = false;

        foreach (var envelope in envelopes)
        {
            var amount = goals.GetValueOrDefault(envelope.Name);
            var auto = entries.FirstOrDefault(x => x.EnvelopeId == envelope.Id && x.IsAuto);

            if (auto is null)
            {
                if (amount <= 0) continue;

                db.SavingsEntries.Add(new SavingsEntry
                {
                    EnvelopeId = envelope.Id,
                    Date = today,
                    Kind = SavingsEntryKind.Deposit,
                    AmountOriginal = amount,
                    CurrencyOriginal = Money.BaseCurrency,
                    AmountBase = amount,
                    FxRate = 1m,
                    FxDate = today,
                    IsAuto = true,
                    Note = $"За схемою «{scheme.Name}»",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                changed = true;
            }
            else if (amount <= 0)
            {
                // Removed, not zeroed: a 0 zł deposit in the envelope's history is a line
                // that says nothing and still has to be read.
                db.SavingsEntries.Remove(auto);
                changed = true;
            }
            else if (auto.AmountBase != amount)
            {
                auto.AmountOriginal = amount;
                auto.AmountBase = amount;
                changed = true;
            }
        }

        if (changed) await db.SaveChangesAsync(ct);
    }

    /// This month's goal per envelope name. A scheme bucket owns the goal when there is one;
    /// otherwise the savings plan feeds the default envelope — two mechanisms reserving for
    /// the same pot at once would hold the money twice.
    private async Task<Dictionary<string, decimal>> GoalsAsync(
        AllocationScheme scheme, decimal monthlyBudget, CancellationToken ct)
    {
        var breakdown = await allocations.BreakdownAsync(monthlyBudget, ct);

        var goals = breakdown.Shares
            .Where(s => s.Kind != BucketKind.Spending)
            .ToDictionary(s => s.Name, s => s.Amount);

        if (!goals.ContainsKey(Envelope.DefaultName))
        {
            var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            var fromPlan = SavingsCalculator.MonthGoal(plan, monthlyBudget);
            if (fromPlan > 0) goals[Envelope.DefaultName] = fromPlan;
        }

        return goals;
    }

    /// Makes sure every non-spending bucket has a pot to actually put money in, and that the
    /// default one always exists. Reading creates rows, like recurring materialization does:
    /// the alternative is a scheme that promises a pension bucket the user cannot deposit to.
    private async Task<List<Envelope>> SyncAsync(AllocationScheme scheme, CancellationToken ct)
    {
        var existing = await db.Envelopes.ToListAsync(ct);
        var byName = existing.ToDictionary(e => e.Name);
        var added = false;

        if (!existing.Any(e => e.IsDefault))
        {
            // Adopt a same-named envelope rather than adding a second one — the unique index
            // on the name would reject it anyway, and the balance belongs to that name.
            if (byName.TryGetValue(Envelope.DefaultName, out var same)) same.IsDefault = true;
            else
            {
                var def = New(Envelope.DefaultName, BucketKind.Savings, isDefault: true);
                db.Envelopes.Add(def);
                existing.Add(def);
                byName[def.Name] = def;
            }
            added = true;
        }

        foreach (var bucket in scheme.Buckets.Where(b => b.Kind != BucketKind.Spending))
        {
            if (byName.ContainsKey(bucket.Name)) continue;

            var e = New(bucket.Name, bucket.Kind, isDefault: false);
            db.Envelopes.Add(e);
            existing.Add(e);
            byName[e.Name] = e;
            added = true;
        }

        if (added) await db.SaveChangesAsync(ct);
        return existing;
    }

    private static Envelope New(string name, BucketKind kind, bool isDefault) => new()
    {
        Name = name, Kind = kind, IsDefault = isDefault, CreatedAt = DateTimeOffset.UtcNow,
    };
}
