namespace FinanceApp.Domain.Tax;

/// Suggested Polish B2B defaults for 2026. These are DATA used to prefill a profile,
/// never used directly by the calculator — the user's stored profile always wins.
/// STALE EVERY YEAR: verify with an accountant / ZUS before relying on them.
public static class PolishTaxDefaults2026
{
    public const int Year = 2026;

    // --- ZUS social, monthly, PLN ---
    public const decimal DuzyWithChorobowe = 1926.76m;
    public const decimal ChorobowePart = 138.57m;
    public const decimal DuzyWithoutChorobowe = DuzyWithChorobowe - ChorobowePart; // 1788.19
    public const decimal PreferencyjnyWithChorobowe = 456.18m;
    public const decimal PreferencyjnyWithoutChorobowe = 420.86m;
    public const decimal UlgaNaStart = 0m; // health only, no social contributions

    // --- Health contribution for RYCZALT, monthly, PLN, by yearly przychód threshold ---
    public const decimal HealthRyczaltUnder60k = 498.35m;
    public const decimal HealthRyczalt60kTo300k = 830.58m;
    public const decimal HealthRyczaltOver300k = 1495.04m;

    public const decimal Threshold60k = 60_000m;
    public const decimal Threshold300k = 300_000m;

    /// Suggested monthly ZUS social for a scheme (user can always override).
    public static decimal SuggestZusSocial(ZusType type, bool chorobowe) => type switch
    {
        ZusType.Duzy => chorobowe ? DuzyWithChorobowe : DuzyWithoutChorobowe,
        ZusType.Preferencyjny => chorobowe ? PreferencyjnyWithChorobowe : PreferencyjnyWithoutChorobowe,
        ZusType.UlgaNaStart => UlgaNaStart,
        _ => DuzyWithChorobowe, // MalyZusPlus depends on prior-year revenue — user enters it
    };

    /// Suggested monthly health contribution on ryczalt for a yearly przychód.
    public static decimal SuggestHealthRyczalt(decimal yearlyRevenue) => yearlyRevenue switch
    {
        < Threshold60k => HealthRyczaltUnder60k,
        < Threshold300k => HealthRyczalt60kTo300k,
        _ => HealthRyczaltOver300k,
    };
}
