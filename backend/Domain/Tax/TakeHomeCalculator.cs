using FinanceApp.Domain.Common;
using static FinanceApp.Domain.Tax.PolishPayrollRates2026;

namespace FinanceApp.Domain.Tax;

/// Full breakdown of one invoice, from what lands on the account to what is actually yours.
public record TakeHomeBreakdown(
    decimal GrossWithVat,
    decimal VatAmount,
    decimal Revenue,          // przychód — VAT excluded, the base for taxes
    decimal ZusSocial,
    decimal HealthContribution,
    decimal HealthDeducted,   // part of health deductible from the tax base (50% on ryczalt)
    decimal TaxBase,
    decimal Tax,
    decimal TakeHome)         // what actually stays with you
{
    /// Everything that lands on the account but is owed to the state.
    /// Invariant: GrossWithVat - SetAside == TakeHome.
    public decimal SetAside => VatAmount + ZusSocial + HealthContribution + Tax;
}

/// The heart of the product: turns what the user earned into real take-home pay.
/// Pure function — no DB, no clock — so it is fully testable.
///
/// A switch over four regimes, deliberately not a strategy hierarchy: the set is small
/// and closed, and the ceremony would cost more than it explains.
public static class TakeHomeCalculator
{
    /// <param name="amount">Amount the user typed (invoice or salary).</param>
    /// <param name="amountIncludesVat">true = user entered gross (with VAT); false = net (przychód).</param>
    public static Result<TakeHomeBreakdown> Calculate(
        TaxProfile profile, decimal amount, bool amountIncludesVat)
    {
        if (amount < 0)
            return Error.Validation("Сума не може бути від'ємною.");

        return profile.Regime switch
        {
            TaxRegime.None => Result<TakeHomeBreakdown>.Ok(NoTax(amount)),
            TaxRegime.Ryczalt => Ryczalt(profile, amount, amountIncludesVat),
            TaxRegime.UoP => Payroll(profile, amount, TaxRegime.UoP),
            TaxRegime.Zlecenie => Payroll(profile, amount, TaxRegime.Zlecenie),
            _ => Error.Unsupported($"Форма оподаткування {profile.Regime} ще не рахується."),
        };
    }

    /// "Just money": nothing is withheld, so every figure collapses to the amount itself.
    private static TakeHomeBreakdown NoTax(decimal amount) => new(
        GrossWithVat: amount, VatAmount: 0m, Revenue: amount,
        ZusSocial: 0m, HealthContribution: 0m, HealthDeducted: 0m,
        TaxBase: amount, Tax: 0m, TakeHome: amount);

    /// Employment income (UoP / zlecenie): gross from the contract to what reaches the account.
    /// Order (PL): gross -> minus employee social contributions -> health on what is left
    /// (never deductible from PIT) -> PIT on gross minus social minus koszty uzyskania.
    ///
    /// VAT plays no part here, so <c>amountIncludesVat</c> is deliberately ignored: on payroll
    /// the amount is always the contract gross.
    private static Result<TakeHomeBreakdown> Payroll(TaxProfile profile, decimal gross, TaxRegime regime)
    {
        // A student under 26 on zlecenie is outside ZUS entirely, and ulga dla młodych
        // clears the PIT — so the gross is the net. UoP gives no such exemption.
        if (regime == TaxRegime.Zlecenie && profile.StudentUnder26)
            return Result<TakeHomeBreakdown>.Ok(NoTax(gross));

        // Chorobowe is mandatory on UoP and voluntary on zlecenie.
        var sickness = regime == TaxRegime.UoP || profile.Chorobowe ? Sickness : 0m;
        var social = Round(gross * (Pension + Disability + sickness));

        var afterSocial = gross - social;
        var health = Round(afterSocial * Health);

        var costs = regime == TaxRegime.UoP
            ? UoPMonthlyCosts
            : Round(afterSocial * ZlecenieCostsRate);
        // The PIT base is rounded to whole zloty, as the tax office requires.
        var taxBase = Math.Max(0m, Math.Round(afterSocial - costs, 0, MidpointRounding.AwayFromZero));

        // Kwota zmniejszająca needs a filed PIT-2, which is the norm on UoP and the exception
        // on zlecenie. Not applying it on zlecenie under-reports take-home rather than over-.
        var relief = regime == TaxRegime.UoP ? MonthlyTaxRelief : 0m;
        var tax = Math.Max(0m, Round(TaxOnMonthlyBase(taxBase) - relief));

        var takeHome = Round(gross - social - health - tax);

        return Result<TakeHomeBreakdown>.Ok(new TakeHomeBreakdown(
            GrossWithVat: gross, VatAmount: 0m, Revenue: gross,
            ZusSocial: social, HealthContribution: health, HealthDeducted: 0m,
            TaxBase: taxBase, Tax: tax, TakeHome: takeHome));
    }

    /// Ryczalt order (PL): VAT is transit money (not income) -> przychód -> minus ZUS social
    /// -> minus 50% of health from the tax base -> tax on that base -> subtract everything.
    private static Result<TakeHomeBreakdown> Ryczalt(
        TaxProfile profile, decimal amount, bool amountIncludesVat)
    {
        var vatRate = profile.VatPayer ? profile.VatRate : 0m;

        // Split VAT out: it never belongs to you, it is passed to the tax office.
        decimal grossWithVat, revenue;
        if (amountIncludesVat)
        {
            grossWithVat = amount;
            revenue = Round(amount / (1 + vatRate));
        }
        else
        {
            revenue = amount;
            grossWithVat = Round(amount * (1 + vatRate));
        }
        var vatAmount = Round(grossWithVat - revenue);

        // On ryczalt: social contributions and 50% of the health contribution
        // reduce the taxable base.
        var healthDeducted = Round(profile.HealthContribution * 0.5m);
        var taxBase = Math.Max(0m, revenue - profile.ZusSocial - healthDeducted);
        var tax = Round(taxBase * profile.RyczaltRate);

        var takeHome = Round(revenue - profile.ZusSocial - profile.HealthContribution - tax);

        return Result<TakeHomeBreakdown>.Ok(new TakeHomeBreakdown(
            grossWithVat, vatAmount, revenue, profile.ZusSocial, profile.HealthContribution,
            healthDeducted, taxBase, tax, takeHome));
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
