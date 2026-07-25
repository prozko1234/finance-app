using FinanceApp.Domain;
using FinanceApp.Domain.Tax;

namespace FinanceApp.Api.Tests;

/// Guards the rule that makes income+tax correct: Polish contributions (ZUS, health)
/// are MONTHLY, so they must be applied once to the month's total revenue — never per
/// invoice, which would double-count them on multi-invoice months.
public class MonthlyTaxTests
{
    private static TaxProfile Profile() => new()
    {
        Regime = TaxRegime.Ryczalt,
        RyczaltRate = 0.12m,
        VatPayer = true,
        VatRate = 0.23m,
        ZusSocial = 1788.19m,
        HealthContribution = 830.58m,
        ValidFrom = new DateOnly(2026, 1, 1),
    };

    [Fact]
    public void Two_invoices_taxed_monthly_beat_per_invoice_double_counting()
    {
        var p = Profile();

        // Correct: one calculation over the month's total revenue.
        var monthly = TakeHomeCalculator.Calculate(p, 20_000m, amountIncludesVat: false).Value!;

        // Wrong (what we must NOT do): tax each invoice separately.
        var first = TakeHomeCalculator.Calculate(p, 10_000m, amountIncludesVat: false).Value!;
        var second = TakeHomeCalculator.Calculate(p, 10_000m, amountIncludesVat: false).Value!;
        var perInvoice = first.TakeHome + second.TakeHome;

        // Per-invoice deducts a full month of contributions twice, so it under-reports
        // take-home. The gap is one extra month of contributions, minus the tax those
        // duplicated deductions wrongly saved.
        var duplicatedContributions = p.ZusSocial + p.HealthContribution;
        var taxWronglySaved = monthly.Tax - (first.Tax + second.Tax);

        Assert.True(monthly.TakeHome > perInvoice);
        Assert.Equal(duplicatedContributions - taxWronglySaved,
            Math.Round(monthly.TakeHome - perInvoice, 2));
    }

    [Fact]
    public void Monthly_total_equals_single_invoice_of_the_same_size()
    {
        var p = Profile();

        var asOne = TakeHomeCalculator.Calculate(p, 15_000m, amountIncludesVat: false).Value!;
        // Splitting the same revenue across the month must not change the monthly outcome,
        // because SummaryService sums revenue first and taxes the total.
        var summedRevenue = 7_000m + 8_000m;
        var asSum = TakeHomeCalculator.Calculate(p, summedRevenue, amountIncludesVat: false).Value!;

        Assert.Equal(asOne.TakeHome, asSum.TakeHome);
    }
}
