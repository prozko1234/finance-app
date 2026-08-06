namespace FinanceApp.Domain.Budgeting;

/// What a bucket is FOR. Drives behaviour, not decoration: everything that is not
/// Spending is held back from "скільки можна витратити сьогодні".
public enum BucketKind { Spending, Savings, Investing, Debt, Other }

/// How the month's budget is divided. There is always exactly one active scheme —
/// the default is a single Spending bucket at 100%, which is the app's original
/// behaviour expressed as a scheme rather than as a special case beside one.
public class AllocationScheme : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    public string Name { get; set; } = "";
    /// Key of the preset this came from, or null when the user built it themselves.
    /// Kept so a preset can be recognised later without comparing percentages.
    public string? Preset { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<AllocationBucket> Buckets { get; set; } = [];
}

/// One share of the budget. Percentages across a scheme must add up to 100 — the
/// service enforces it, because a scheme that does not add up would silently lose money.
public class AllocationBucket : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }
    public int SchemeId { get; set; }
    public AllocationScheme? Scheme { get; set; }

    public string Name { get; set; } = "";
    public BucketKind Kind { get; set; }
    /// Whole-percent share of the month's budget, e.g. 30 for 30%.
    public decimal Percent { get; set; }
    public int SortOrder { get; set; }
}
