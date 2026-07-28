using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Common;
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
    Task<IReadOnlyList<EnvelopeStatus>> StatusAsync(decimal monthlyBudget, CancellationToken ct = default);
}

public sealed class EnvelopeService(IAppDbContext db, IAllocationService allocations) : IEnvelopeService
{
    public async Task<IReadOnlyList<EnvelopeStatus>> StatusAsync(
        decimal monthlyBudget, CancellationToken ct = default)
    {
        var scheme = await allocations.GetActiveAsync(ct);
        var goals = await GoalsAsync(scheme, monthlyBudget, ct);
        var envelopes = await SyncAsync(scheme, ct);

        var balances = await db.SavingsEntries
            .GroupBy(x => x.EnvelopeId)
            .Select(g => new
            {
                EnvelopeId = g.Key,
                Balance = g.Sum(x => x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase),
            })
            .ToDictionaryAsync(x => x.EnvelopeId, x => x.Balance, ct);

        var (first, last) = MonthRange.Of(DateOnly.FromDateTime(DateTime.Now));
        var thisMonth = await db.SavingsEntries
            .Where(x => x.Date >= first && x.Date <= last)
            .GroupBy(x => x.EnvelopeId)
            .Select(g => new
            {
                EnvelopeId = g.Key,
                Net = g.Sum(x => x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase),
            })
            .ToDictionaryAsync(x => x.EnvelopeId, x => x.Net, ct);

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
