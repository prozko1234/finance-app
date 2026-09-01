namespace FinanceApp.Domain;

using FinanceApp.Domain.Tax;

/// User's taxation setup. Rates and contribution amounts are DATA, never hardcoded in
/// the calculator — Polish rates change every year, so the user (or a new profile row
/// with a later ValidFrom) can update them without a code change.
public class TaxProfile : IOwnedByUser
{
    public int Id { get; set; }

    /// The account this row belongs to. Set by the context, never by a service.
    public int UserId { get; set; }

    /// None by default: a new user is not assumed to be a B2B contractor. The remaining
    /// defaults below are prefills for the form once a business regime is chosen.
    public TaxRegime Regime { get; set; } = TaxRegime.None;
    /// Ryczalt rate as a fraction, e.g. 0.12 for 12%.
    public decimal RyczaltRate { get; set; } = 0.12m;

    public bool VatPayer { get; set; } = true;
    /// VAT rate as a fraction, e.g. 0.23 for 23%.
    public decimal VatRate { get; set; } = 0.23m;

    public ZusType ZusType { get; set; } = ZusType.Duzy;
    /// Monthly ZUS social contributions in PLN (editable — this is what the accountant bills).
    public decimal ZusSocial { get; set; }
    /// Monthly health contribution in PLN (ryczalt: fixed per income threshold).
    public decimal HealthContribution { get; set; }
    /// Whether voluntary sickness insurance (chorobowe) is included in ZusSocial.
    /// On zlecenie this also decides whether the contribution is withheld at all.
    public bool Chorobowe { get; set; }

    /// Zlecenie only: a student under 26 pays no ZUS and no PIT (ulga dla młodych).
    /// Common enough in this app's audience to be worth a switch rather than a wrong number.
    public bool StudentUnder26 { get; set; }

    /// Year these amounts apply to — rates go stale annually.
    /// The VAT rate that actually applies, which is not the same thing as the rate stored.
    ///
    /// <see cref="VatPayer"/> defaults to true and is remembered independently of the regime,
    /// so that switching away from ryczałt and back does not lose the setting. Nothing may read
    /// it on its own: under "Просто гроші" and under both employment contracts there is no VAT
    /// at all — <see cref="TakeHomeCalculator"/> has always said so, returning a zero VAT line
    /// for them.
    ///
    /// Income storage read the flag instead and stripped 23% from every figure typed, while the
    /// same screen reported "VAT 0,00 zł". A real user lost 2 950 zł that way over one period:
    /// the money vanished between writing it down and reading it back, and nothing on screen
    /// could explain where it went.
    public decimal EffectiveVatRate =>
        Regime == TaxRegime.Ryczalt && VatPayer ? VatRate : 0m;

    public DateOnly ValidFrom { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
