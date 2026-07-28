using FinanceApp.Application.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Tax;

namespace FinanceApp.Application.Mapping;

public static class TaxMapping
{
    public static MonthTaxBreakdown ToMonthBreakdown(this TakeHomeBreakdown b) => new(
        b.GrossWithVat, b.Revenue, b.VatAmount, b.ZusSocial, b.HealthContribution,
        b.Tax, b.SetAside, b.TakeHome, Money.BaseCurrency, PolishTaxDefaults2026.Year);
}
