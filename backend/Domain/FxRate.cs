namespace FinanceApp.Domain;

/// Rate cache: how many PLN per 1 unit of currency for a requested date.
/// Avoids hitting the external API again for the same (currency, date) pair.
public class FxRate
{
    public required string Currency { get; set; }   // ISO code, e.g. USD
    public DateOnly Date { get; set; }               // requested transaction date
    public decimal PlnPerUnit { get; set; }
    public DateOnly EffectiveDate { get; set; }      // actual rate date (may be earlier)
    public required string Source { get; set; }      // NBP / ECB
    public DateTimeOffset FetchedAt { get; set; }
}
