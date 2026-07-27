using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Api.Tests;

/// Splitting the budget has one rule that must never bend: the shares add up to the
/// budget exactly. A scheme that loses or invents a cent every month is worse than no
/// scheme at all, because the error compounds silently.
public class AllocationTests
{
    private static List<AllocationBucket> Buckets(params (string name, BucketKind kind, decimal pct)[] items) =>
        items.Select((b, i) => new AllocationBucket
        {
            Id = i + 1, Name = b.name, Kind = b.kind, Percent = b.pct, SortOrder = i,
        }).ToList();

    [Fact]
    public void Shares_add_up_to_the_budget()
    {
        var shares = AllocationCalculator.Split(
            10_000m, Buckets(("Потреби", BucketKind.Spending, 50m), ("Бажання", BucketKind.Spending, 30m),
                             ("Заощадження", BucketKind.Savings, 20m)));

        Assert.Equal(10_000m, shares.Sum(s => s.Amount));
        Assert.Equal(5_000m, shares[0].Amount);
        Assert.Equal(2_000m, shares[2].Amount);
    }

    /// 3333.33 × 3 = 9999.99: the lost cent has to land somewhere, and it lands on Spending.
    [Fact]
    public void Rounding_leftovers_go_to_spending_not_into_thin_air()
    {
        var shares = AllocationCalculator.Split(
            10_000m, Buckets(("A", BucketKind.Savings, 33.33m), ("Витрати", BucketKind.Spending, 33.34m),
                             ("B", BucketKind.Investing, 33.33m)));

        Assert.Equal(10_000m, shares.Sum(s => s.Amount));
        Assert.Equal(BucketKind.Spending, shares.MaxBy(s => s.Amount)!.Kind);
    }

    /// Without a Spending bucket there is nowhere natural to put the remainder, but the
    /// total still has to match — the first bucket takes it.
    [Fact]
    public void Total_holds_even_with_no_spending_bucket()
    {
        var shares = AllocationCalculator.Split(
            999.99m, Buckets(("A", BucketKind.Savings, 50m), ("B", BucketKind.Debt, 50m)));

        Assert.Equal(999.99m, shares.Sum(s => s.Amount));
    }

    [Fact]
    public void Spendable_is_the_spending_buckets_and_reserved_is_the_rest()
    {
        var shares = AllocationCalculator.Split(
            10_000m, Buckets(("Потреби", BucketKind.Spending, 50m), ("Бажання", BucketKind.Spending, 30m),
                             ("Заощадження", BucketKind.Savings, 20m)));

        Assert.Equal(8_000m, AllocationCalculator.Spendable(shares));
        Assert.Equal(2_000m, AllocationCalculator.Reserved(shares));
    }

    /// The default scheme must reproduce the app's behaviour before schemes existed:
    /// the whole budget is spendable, nothing is held back.
    [Fact]
    public void The_default_preset_changes_nothing()
    {
        var scheme = AllocationPresets.Find(AllocationPresets.DailyNormOnly)!.ToScheme(isActive: true);

        var shares = AllocationCalculator.Split(7_531.17m, scheme.Buckets);

        Assert.Equal(7_531.17m, AllocationCalculator.Spendable(shares));
        Assert.Equal(0m, AllocationCalculator.Reserved(shares));
    }

    [Fact]
    public void Every_preset_adds_up_to_100()
    {
        foreach (var preset in AllocationPresets.All)
        {
            var scheme = preset.ToScheme(isActive: false);
            Assert.True(AllocationCalculator.AddsUpTo100(scheme.Buckets), $"{preset.Key} не дає 100%");
        }
    }

    [Fact]
    public void A_budget_of_zero_produces_zero_shares_not_a_crash()
    {
        var shares = AllocationCalculator.Split(0m, Buckets(("A", BucketKind.Spending, 100m)));

        Assert.Equal(0m, Assert.Single(shares).Amount);
    }
}
