namespace FinanceApp.Domain;

/// "This shop goes in this category", remembered.
///
/// Written whenever the user files an imported row, so the correction is made once and never
/// again. It is what turns the importer from a form into something that gets out of the way:
/// by the third statement most rows arrive already filed.
///
/// Learned rules outrank the built-in merchant list on purpose — the list is a guess about
/// people in general, this is a fact about this person.
public class MerchantRule : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    /// The normalized shop name (see <see cref="Import.MerchantKey"/>), not the raw
    /// description: the raw text carries a branch number that changes every visit.
    public required string Key { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    /// How often it has matched. Not used to decide anything yet — it is here so a rules
    /// screen can one day show what is actually earning its place.
    public int Hits { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    /// Long enough for the longest real chain name, short enough to stay an index.
    public const int MaxKeyLength = 60;
}
