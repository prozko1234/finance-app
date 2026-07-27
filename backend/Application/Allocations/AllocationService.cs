using FinanceApp.Application.Abstractions;
using FinanceApp.Domain.Budgeting;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Allocations;

/// The active scheme applied to a concrete budget.
/// <param name="SavingsGoal">Sum of the Savings buckets, or null when the scheme has none —
/// null means "the savings plan still decides the goal".</param>
public record AllocationBreakdown(
    string SchemeName,
    string? Preset,
    IReadOnlyList<BucketShare> Shares,
    decimal Spendable,
    decimal Reserved,
    decimal? SavingsGoal);

public interface IAllocationService
{
    /// The one active scheme, buckets included.
    Task<AllocationScheme> GetActiveAsync(CancellationToken ct = default);

    /// How this month's budget divides across the active scheme.
    Task<AllocationBreakdown> BreakdownAsync(decimal budget, CancellationToken ct = default);
}

public sealed class AllocationService(IAppDbContext db) : IAllocationService
{
    public async Task<AllocationScheme> GetActiveAsync(CancellationToken ct = default)
    {
        var scheme = await db.AllocationSchemes
            .Include(s => s.Buckets)
            .FirstOrDefaultAsync(s => s.IsActive, ct);

        // A database is seeded with the default scheme, so this is only reachable if someone
        // deleted it. Fall back to the preset in memory rather than failing the summary —
        // the whole app hangs off this number.
        return scheme ?? AllocationPresets
            .Find(AllocationPresets.DailyNormOnly)!
            .ToScheme(isActive: true);
    }

    public async Task<AllocationBreakdown> BreakdownAsync(decimal budget, CancellationToken ct = default)
    {
        var scheme = await GetActiveAsync(ct);
        var shares = AllocationCalculator.Split(budget, scheme.Buckets);

        var savings = shares.Where(s => s.Kind == BucketKind.Savings).ToList();

        return new AllocationBreakdown(
            scheme.Name,
            scheme.Preset,
            shares,
            AllocationCalculator.Spendable(shares),
            AllocationCalculator.Reserved(shares),
            savings.Count == 0 ? null : savings.Sum(s => s.Amount));
    }
}
