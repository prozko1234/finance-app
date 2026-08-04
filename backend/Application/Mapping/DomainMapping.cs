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
        t.CategoryId, t.Category?.Name ?? "", t.EnvelopeId, t.Envelope?.Name, t.Frequency, t.Source.ToString(),
        t.Date, t.MerchantRaw, t.Note, t.CreatedAt, t.AmountBase, Money.BaseCurrency,
        TypedGross(t), t.Category?.Icon);

    /// Was the typed figure the gross one? What the user entered, converted to base, is either
    /// the gross or the revenue, and those differ by the whole VAT — so comparing against the
    /// stored gross recovers the answer without keeping a flag in the database for it.
    private static bool TypedGross(Transaction t) =>
        t.Kind == TransactionKind.Income
        && t.GrossWithVat is { } gross
        && Math.Abs(t.AmountOriginal * t.FxRate - gross) < 0.02m;

    public static CategoryResponse ToResponse(this Category c) =>
        new(c.Id, c.Name, c.Icon, c.Color, c.SortOrder, c.IsSystem);

    public static RecurringResponse ToResponse(this RecurringExpense r) => new(
        r.Id, r.AmountOriginal, r.CurrencyOriginal, r.CategoryId, r.Category?.Name ?? "",
        r.StartsOn, r.Unit.ToString(), r.Interval, r.Active, r.Note, r.Kind.ToString(), r.AmountIncludesVat);
}
