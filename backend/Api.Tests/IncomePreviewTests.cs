using FinanceApp.Application.Contracts;
using FinanceApp.Application.Tax;
using FinanceApp.Domain;
using FinanceApp.Domain.Tax;

namespace FinanceApp.Api.Tests;

/// The preview shown while typing an invoice must agree with the home screen.
/// That only holds if it is a MONTHLY delta — contributions are monthly, not per invoice.
public class IncomePreviewTests
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

    private static Transaction Income(decimal revenue, DateOnly date, int categoryId) => new()
    {
        Kind = TransactionKind.Income,
        CategoryId = categoryId,
        AmountOriginal = revenue,
        CurrencyOriginal = "PLN",
        AmountBase = revenue,
        FxRate = 1m,
        FxDate = date,
        Date = date,
        Priority = Priority.Must,
        Frequency = Frequency.OneOff,
    };

    [Fact]
    public async Task First_invoice_of_the_month_splits_vat_out_of_the_gross()
    {
        using var mem = new SqliteInMemory();
        mem.Db.TaxProfiles.Add(Profile());
        await mem.Db.SaveChangesAsync();
        var svc = new TaxService(mem.Db);

        // 24 600 brutto = 20 000 przychód + 4 600 VAT.
        var r = await svc.PreviewIncomeAsync(new CalculateTakeHomeRequest(24_600m, AmountIncludesVat: true));

        Assert.True(r.IsSuccess);
        var p = r.Value!;
        Assert.Equal(24_600m, p.InvoiceGross);
        Assert.Equal(4_600m, p.InvoiceVat);
        Assert.Equal(20_000m, p.InvoiceRevenue);
        Assert.True(p.IsFirstIncomeThisMonth);
        Assert.Equal(0m, p.BudgetBefore);
        Assert.Equal(p.BudgetAfter, p.BudgetDelta); // nothing before, so the whole budget is new
        Assert.Equal(p.BudgetAfter, p.MonthAfter.TakeHome);
    }

    [Fact]
    public async Task Second_invoice_adds_more_than_the_first_because_contributions_are_monthly()
    {
        using var mem = new SqliteInMemory();
        mem.Db.TaxProfiles.Add(Profile());
        await mem.Db.SaveChangesAsync();
        var svc = new TaxService(mem.Db);

        var firstPreview = await svc.PreviewIncomeAsync(new CalculateTakeHomeRequest(10_000m, false));

        // Now that invoice is saved, so ZUS and health are already covered this month.
        var cat = new Category { Name = "Дохід" };
        mem.Db.Categories.Add(cat);
        await mem.Db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Now);
        mem.Db.Transactions.Add(Income(10_000m, today, cat.Id));
        await mem.Db.SaveChangesAsync();

        var secondPreview = await svc.PreviewIncomeAsync(new CalculateTakeHomeRequest(10_000m, false));

        Assert.False(secondPreview.Value!.IsFirstIncomeThisMonth);
        Assert.True(secondPreview.Value!.BudgetDelta > firstPreview.Value!.BudgetDelta);

        // And the month total still equals taxing 20 000 once — same rule as the home screen.
        var monthly = TakeHomeCalculator.Calculate(Profile(), 20_000m, amountIncludesVat: false).Value!;
        Assert.Equal(monthly.TakeHome, secondPreview.Value!.MonthAfter.TakeHome);
        Assert.Equal(monthly.TakeHome, firstPreview.Value!.BudgetDelta + secondPreview.Value!.BudgetDelta);
    }
}
