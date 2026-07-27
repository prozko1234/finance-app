namespace FinanceApp.Domain.Tax;

/// Statutory rates for employment income (UoP, zlecenie) in 2026. Unlike the B2B amounts
/// in <see cref="PolishTaxDefaults2026"/> — which the user negotiates and edits — these are
/// set by law and identical for everyone, so the calculator uses them directly.
/// Verified 2026-07: unchanged from previous years. RECHECK EVERY JANUARY.
public static class PolishPayrollRates2026
{
    // --- Employee-financed social contributions, share of gross ---
    public const decimal Pension = 0.0976m;    // emerytalne
    public const decimal Disability = 0.015m;  // rentowe
    public const decimal Sickness = 0.0245m;   // chorobowe — mandatory on UoP, voluntary on zlecenie

    /// Health contribution, charged on gross minus social. Not deductible from PIT since 2022.
    public const decimal Health = 0.09m;

    // --- PIT, tax scale ---
    public const decimal FirstBracketRate = 0.12m;
    public const decimal SecondBracketRate = 0.32m;
    /// Yearly income above which 32% applies. Monthly advances use a twelfth of it.
    public const decimal BracketThreshold = 120_000m;
    /// Kwota zmniejszająca podatek: 3600/year, taken as 300 per month on a filed PIT-2.
    public const decimal MonthlyTaxRelief = 300m;

    /// Koszty uzyskania on UoP: flat 250/month for someone working in their own town.
    public const decimal UoPMonthlyCosts = 250m;
    /// Koszty uzyskania on zlecenie: 20% of gross after social contributions.
    public const decimal ZlecenieCostsRate = 0.20m;

    /// Monthly PIT advance for a taxable base, before the tax-reducing amount.
    /// Simplification: the 32% bracket is applied per month against a twelfth of the yearly
    /// threshold. Real advances switch when income-to-date crosses it, so someone with an
    /// uneven year sees a small difference — the yearly settlement corrects it either way.
    public static decimal TaxOnMonthlyBase(decimal monthlyBase)
    {
        var monthlyThreshold = BracketThreshold / 12m;
        return monthlyBase <= monthlyThreshold
            ? monthlyBase * FirstBracketRate
            : (monthlyThreshold * FirstBracketRate) + ((monthlyBase - monthlyThreshold) * SecondBracketRate);
    }
}
