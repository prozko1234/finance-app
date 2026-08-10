namespace FinanceApp.Domain.Tax;

/// What the bookkeeper actually said, for one month.
///
/// The engine computes ZUS, the health contribution and PIT from a profile, and it is right
/// often enough to be worth having — but it is a model, and the figure that gets paid comes
/// from a person with the full picture: a month with a sick note, a deduction the app knows
/// nothing about, the year the rates changed before the code did. Until now the only way to
/// reconcile the two was to stop believing the app.
///
/// One row per month, not per invoice: Polish contributions are monthly, and an override
/// hanging off one invoice out of three would be a figure with no month behind it.
///
/// Every component is nullable and means "use the engine's". A month with only ZUS filled in
/// keeps the computed health and PIT — the bookkeeper rarely hands over all three at once.
public class TaxActuals : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    /// The first day of the month these figures are for.
    public DateOnly Month { get; set; }

    public decimal? ZusSocial { get; set; }
    public decimal? Health { get; set; }
    public decimal? Pit { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// True when the row says nothing at all — which is how "clear it" is expressed, and the
    /// signal to delete it rather than keep an empty override around forever.
    public bool IsEmpty => ZusSocial is null && Health is null && Pit is null;
}
