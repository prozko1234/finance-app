using FinanceApp.Domain.Common;

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
            _ => Error.Unsupported(
                "UoP і zlecenie ще рахуються — поки що обери «просто гроші» або ryczałt."),
        };
    }

    /// "Just money": nothing is withheld, so every figure collapses to the amount itself.
    private static TakeHomeBreakdown NoTax(decimal amount) => new(
        GrossWithVat: amount, VatAmount: 0m, Revenue: amount,
        ZusSocial: 0m, HealthContribution: 0m, HealthDeducted: 0m,
        TaxBase: amount, Tax: 0m, TakeHome: amount);

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
