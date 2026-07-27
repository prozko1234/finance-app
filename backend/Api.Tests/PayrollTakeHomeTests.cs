using FinanceApp.Domain;
using FinanceApp.Domain.Tax;

namespace FinanceApp.Api.Tests;

/// Employment income: gross from the contract to what actually reaches the account.
/// Figures below are computed by hand from the 2026 statutory rates — if a rate changes,
/// these tests fail first, which is exactly what should happen.
public class PayrollTakeHomeTests
{
    private static TaxProfile Profile(TaxRegime regime) => new()
    {
        Regime = regime,
        ValidFrom = new DateOnly(2026, 1, 1),
    };

    /// 10 000 brutto on UoP:
    /// social = 10000 * 13.71% = 1371.00 · health = 8629 * 9% = 776.61
    /// base = round(8629 - 250) = 8379 · tax = 8379 * 12% - 300 = 705.48
    /// net = 10000 - 1371 - 776.61 - 705.48 = 7146.91
    [Fact]
    public void UoP_gross_to_net()
    {
        var b = TakeHomeCalculator.Calculate(Profile(TaxRegime.UoP), 10_000m, amountIncludesVat: false).Value!;

        Assert.Equal(1371.00m, b.ZusSocial);
        Assert.Equal(776.61m, b.HealthContribution);
        Assert.Equal(8379m, b.TaxBase);
        Assert.Equal(705.48m, b.Tax);
        Assert.Equal(7146.91m, b.TakeHome);
        Assert.Equal(0m, b.VatAmount);
    }

    /// On payroll the amount is the contract gross, so the brutto/netto switch from the
    /// B2B form must not change a single figure.
    [Fact]
    public void UoP_ignores_the_vat_switch()
    {
        var withVat = TakeHomeCalculator.Calculate(Profile(TaxRegime.UoP), 10_000m, amountIncludesVat: true).Value!;
        var without = TakeHomeCalculator.Calculate(Profile(TaxRegime.UoP), 10_000m, amountIncludesVat: false).Value!;

        Assert.Equal(without, withVat);
    }

    /// Zlecenie without chorobowe: social = 5000 * 11.26% = 563.00
    /// health = 4437 * 9% = 399.33 · costs = 4437 * 20% = 887.40
    /// base = round(4437 - 887.40) = 3550 · tax = 3550 * 12% = 426.00 (no PIT-2 relief)
    /// net = 5000 - 563 - 399.33 - 426 = 3611.67
    [Fact]
    public void Zlecenie_gross_to_net()
    {
        var b = TakeHomeCalculator.Calculate(Profile(TaxRegime.Zlecenie), 5_000m, amountIncludesVat: false).Value!;

        Assert.Equal(563.00m, b.ZusSocial);
        Assert.Equal(399.33m, b.HealthContribution);
        Assert.Equal(426.00m, b.Tax);
        Assert.Equal(3611.67m, b.TakeHome);
    }

    /// The audience this matters most to: a student on zlecenie is outside ZUS and PIT,
    /// so the gross is the net. Getting this wrong would show them a number ~28% too low.
    [Fact]
    public void Student_under_26_on_zlecenie_keeps_everything()
    {
        var p = Profile(TaxRegime.Zlecenie);
        p.StudentUnder26 = true;

        var b = TakeHomeCalculator.Calculate(p, 5_000m, amountIncludesVat: false).Value!;

        Assert.Equal(5_000m, b.TakeHome);
        Assert.Equal(0m, b.SetAside);
    }

    /// The exemption is a zlecenie rule — an employment contract gives no such relief.
    [Fact]
    public void Student_flag_does_not_leak_into_UoP()
    {
        var p = Profile(TaxRegime.UoP);
        p.StudentUnder26 = true;

        var b = TakeHomeCalculator.Calculate(p, 10_000m, amountIncludesVat: false).Value!;

        Assert.Equal(7146.91m, b.TakeHome);
    }

    /// Chorobowe is voluntary on zlecenie, so opting in costs 2.45% of gross in contributions.
    [Fact]
    public void Chorobowe_on_zlecenie_is_optional()
    {
        var p = Profile(TaxRegime.Zlecenie);
        p.Chorobowe = true;

        var with = TakeHomeCalculator.Calculate(p, 5_000m, amountIncludesVat: false).Value!;
        var without = TakeHomeCalculator.Calculate(Profile(TaxRegime.Zlecenie), 5_000m, amountIncludesVat: false).Value!;

        Assert.Equal(122.50m, with.ZusSocial - without.ZusSocial);
        Assert.True(with.TakeHome < without.TakeHome);
    }

    /// Above a twelfth of the yearly threshold the surplus is taxed at 32%, not 12%.
    [Fact]
    public void Second_bracket_applies_above_the_monthly_threshold()
    {
        var justUnder = PolishPayrollRates2026.TaxOnMonthlyBase(10_000m);
        var justOver = PolishPayrollRates2026.TaxOnMonthlyBase(11_000m);

        Assert.Equal(1_200m, justUnder);
        Assert.Equal(1_200m + 320m, justOver);
    }

    /// The invariant every regime must hold: what you keep plus what is withheld is the whole.
    [Theory]
    [InlineData(TaxRegime.UoP)]
    [InlineData(TaxRegime.Zlecenie)]
    public void Nothing_goes_missing(TaxRegime regime)
    {
        var b = TakeHomeCalculator.Calculate(Profile(regime), 7_777.77m, amountIncludesVat: false).Value!;

        Assert.Equal(b.TakeHome, b.GrossWithVat - b.SetAside);
    }
}
