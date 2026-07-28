using FinanceApp.Domain.Budgeting;

namespace FinanceApp.Domain.Savings;

/// A pot that money is actually moved into: pension, wishes, a cushion, debt repayment.
///
/// Before envelopes, a scheme's non-Spending buckets only ever subtracted from safe-to-spend
/// — the app held the money back every month and could never say where it went or how much
/// had piled up. A bucket is this month's INTENTION; an envelope is the BALANCE that
/// intention builds, and it survives the month.
///
/// Identified by name rather than by bucket id on purpose: saving a scheme deletes and
/// recreates its buckets, so bucket ids do not survive an edit — a balance hanging off one
/// would vanish the first time the user changed a percentage.
public class Envelope
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
    public BucketKind Kind { get; set; } = BucketKind.Savings;

    /// The envelope that exists even with no scheme at all, fed by the savings plan.
    /// Cannot be removed by editing a scheme, so there is always somewhere to put money.
    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// Name of the default envelope, and the one the savings plan feeds.
    public const string DefaultName = "Заощадження";
}
