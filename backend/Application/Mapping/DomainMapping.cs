using FinanceApp.Application.Contracts;
using FinanceApp.Domain;

namespace FinanceApp.Application.Mapping;

/// Centralized manual domain → response DTO mapping. A single source of truth,
/// explicit and reflection-free (AutoMapper here would be needless magic and hidden cost).
public static class DomainMapping
{
    /// Reads in base currency. Callers that know the user's display currency override
    /// AmountDisplay/DisplayCurrency — the amount is never left labelled the wrong way.
    public static TransactionResponse ToResponse(this Transaction t) => new(
        t.Id, t.Kind.ToString(), t.GrossWithVat, t.VatAmount, t.AmountOriginal, t.CurrencyOriginal, t.AmountBase, t.FxRate, t.FxDate,
        t.CategoryId, t.Category?.Name ?? "", t.Priority, t.Frequency, t.Source.ToString(),
        t.Date, t.MerchantRaw, t.Note, t.CreatedAt, t.AmountBase, Money.BaseCurrency);

    public static CategoryResponse ToResponse(this Category c) =>
        new(c.Id, c.Name, c.Icon, c.Color, c.SortOrder, c.IsSystem);

    public static BudgetResponse ToResponse(this Budget? b) => b is null
        ? new BudgetResponse(false, null, Money.BaseCurrency, null)
        : new BudgetResponse(true, b.MonthlyAmount, Money.BaseCurrency, b.UpdatedAt);

    public static RecurringResponse ToResponse(this RecurringExpense r) => new(
        r.Id, r.AmountOriginal, r.CurrencyOriginal, r.CategoryId, r.Category?.Name ?? "",
        r.DayOfMonth, r.Active, r.Note, r.Kind.ToString(), r.AmountIncludesVat);
}
