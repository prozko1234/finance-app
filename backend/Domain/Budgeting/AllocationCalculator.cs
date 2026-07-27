namespace FinanceApp.Domain.Budgeting;

/// One bucket with the money that actually falls into it this month.
public record BucketShare(int BucketId, string Name, BucketKind Kind, decimal Percent, decimal Amount);

/// Splits the month's budget across a scheme's buckets.
/// Pure function — no DB, no clock — so the one rule that matters can be tested directly:
/// the shares always add up to the budget, to the cent.
public static class AllocationCalculator
{
    public static IReadOnlyList<BucketShare> Split(decimal budget, IEnumerable<AllocationBucket> buckets)
    {
        var ordered = buckets.OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToList();
        if (ordered.Count == 0) return [];

        // Round every share DOWN first, then hand the leftover cents to one bucket. Rounding
        // each to nearest independently can add up to more than the budget — money the user
        // does not have. The remainder goes to Spending, where being off by a cent is visible
        // and harmless, rather than into a savings target that would never be reachable.
        var shares = ordered
            .Select(b => new BucketShare(b.Id, b.Name, b.Kind, b.Percent, FloorTo2(budget * b.Percent / 100m)))
            .ToList();

        var remainder = budget - shares.Sum(s => s.Amount);
        if (remainder != 0m)
        {
            var i = shares.FindIndex(s => s.Kind == BucketKind.Spending);
            if (i < 0) i = 0;
            shares[i] = shares[i] with { Amount = shares[i].Amount + remainder };
        }

        return shares;
    }

    /// What may actually be spent day to day — the Spending buckets only.
    public static decimal Spendable(IEnumerable<BucketShare> shares) =>
        shares.Where(s => s.Kind == BucketKind.Spending).Sum(s => s.Amount);

    /// Everything the scheme holds back: savings, investing, debt, other.
    public static decimal Reserved(IEnumerable<BucketShare> shares) =>
        shares.Where(s => s.Kind != BucketKind.Spending).Sum(s => s.Amount);

    /// A scheme only makes sense if its buckets account for the whole budget.
    public static bool AddsUpTo100(IEnumerable<AllocationBucket> buckets) =>
        buckets.Sum(b => b.Percent) == 100m;

    private static decimal FloorTo2(decimal v) => Math.Floor(v * 100m) / 100m;
}
