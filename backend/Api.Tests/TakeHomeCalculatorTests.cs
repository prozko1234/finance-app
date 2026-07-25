using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Tax;

namespace FinanceApp.Api.Tests;

public class TakeHomeCalculatorTests
{
    /// Bohdan's real setup: ryczalt 12%, VAT 23%, duzy ZUS without chorobowe,
    /// health at the middle threshold. Accountant bills 2618.77 = 1788.19 + 830.58.
    private static TaxProfile Profile() => new()
    {
        Regime = TaxRegime.Ryczalt,
        RyczaltRate = 0.12m,
        VatPayer = true,
        VatRate = 0.23m,
        ZusType = ZusType.Duzy,
        ZusSocial = 1788.19m,
        HealthContribution = 830.58m,
        Chorobowe = false,
        ValidFrom = new DateOnly(2026, 1, 1),
    };

    [Fact]
    public void Accountant_total_matches_profile_contributions()
    {
        var p = Profile();
        Assert.Equal(2618.77m, p.ZusSocial + p.HealthContribution);
    }

    [Fact]
    public void Net_input_20000_full_breakdown()
    {
        var r = TakeHomeCalculator.Calculate(Profile(), 20_000m, amountIncludesVat: false);

        Assert.True(r.IsSuccess);
        var b = r.Value!;
        Assert.Equal(20_000m, b.Revenue);
        Assert.Equal(24_600m, b.GrossWithVat);      // + 23% VAT
        Assert.Equal(4_600m, b.VatAmount);          // transit, not income
        Assert.Equal(415.29m, b.HealthDeducted);    // 50% of 830.58
        Assert.Equal(17_796.52m, b.TaxBase);        // 20000 - 1788.19 - 415.29
        Assert.Equal(2_135.58m, b.Tax);             // 12%
        Assert.Equal(15_245.65m, b.TakeHome);       // 20000 - 1788.19 - 830.58 - 2135.58
    }

    [Fact]
    public void Gross_input_is_the_mirror_of_net_input()
    {
        var fromGross = TakeHomeCalculator.Calculate(Profile(), 24_600m, amountIncludesVat: true).Value!;
        var fromNet = TakeHomeCalculator.Calculate(Profile(), 20_000m, amountIncludesVat: false).Value!;

        Assert.Equal(fromNet.Revenue, fromGross.Revenue);
        Assert.Equal(fromNet.TakeHome, fromGross.TakeHome);
    }

    [Fact]
    public void Vat_is_excluded_from_income_entirely()
    {
        var b = TakeHomeCalculator.Calculate(Profile(), 12_300m, amountIncludesVat: true).Value!;

        Assert.Equal(10_000m, b.Revenue);
        Assert.Equal(2_300m, b.VatAmount);
        // VAT never reaches take-home: it is gross minus revenue, nothing else.
        Assert.Equal(b.GrossWithVat - b.Revenue, b.VatAmount);
    }

    [Fact]
    public void Non_vat_payer_has_no_vat_split()
    {
        var p = Profile();
        p.VatPayer = false;

        var b = TakeHomeCalculator.Calculate(p, 10_000m, amountIncludesVat: false).Value!;

        Assert.Equal(0m, b.VatAmount);
        Assert.Equal(10_000m, b.GrossWithVat);
    }

    [Fact]
    public void Low_revenue_month_does_not_produce_negative_tax_base()
    {
        var b = TakeHomeCalculator.Calculate(Profile(), 1_000m, amountIncludesVat: false).Value!;

        Assert.Equal(0m, b.TaxBase); // contributions exceed revenue -> base floored at 0
        Assert.Equal(0m, b.Tax);
        Assert.True(b.TakeHome < 0); // honest: that month costs more than it earns
    }

    /// The number the home screen shows to explain "why is the budget less than the balance".
    [Fact]
    public void Set_aside_accounts_for_the_whole_gap_between_account_and_budget()
    {
        var b = TakeHomeCalculator.Calculate(Profile(), 20_000m, amountIncludesVat: false).Value!;

        Assert.Equal(b.VatAmount + b.ZusSocial + b.HealthContribution + b.Tax, b.SetAside);
        Assert.Equal(b.TakeHome, b.GrossWithVat - b.SetAside);
    }

    [Fact]
    public void Unsupported_regime_fails_explicitly()
    {
        var p = Profile();
        p.Regime = TaxRegime.Liniowy;

        var r = TakeHomeCalculator.Calculate(p, 10_000m, amountIncludesVat: false);

        Assert.False(r.IsSuccess);
        Assert.Equal(ErrorType.Unsupported, r.Error.Type);
    }

    [Theory]
    [InlineData(50_000, 498.35)]
    [InlineData(120_000, 830.58)]
    [InlineData(400_000, 1495.04)]
    public void Health_threshold_suggestions_2026(decimal yearlyRevenue, decimal expected)
    {
        Assert.Equal(expected, PolishTaxDefaults2026.SuggestHealthRyczalt(yearlyRevenue));
    }

    [Fact]
    public void Duzy_without_chorobowe_suggestion_matches_accountant_number()
    {
        Assert.Equal(1788.19m, PolishTaxDefaults2026.SuggestZusSocial(ZusType.Duzy, chorobowe: false));
    }
}
