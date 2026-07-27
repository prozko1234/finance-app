using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Savings;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Tax;

public interface ITaxService
{
    Task<TaxProfileResponse> GetProfileAsync(CancellationToken ct = default);
    Task<Result<TaxProfileResponse>> SaveProfileAsync(SaveTaxProfileRequest req, CancellationToken ct = default);
    Task<Result<IncomePreviewResponse>> PreviewIncomeAsync(CalculateTakeHomeRequest req, CancellationToken ct = default);
    TaxDefaultsResponse GetDefaults();
}

public sealed class TaxService(IAppDbContext db) : ITaxService
{
    public async Task<TaxProfileResponse> GetProfileAsync(CancellationToken ct = default)
    {
        var p = await LoadOrDefaultAsync(ct);
        return ToResponse(p);
    }

    public async Task<Result<TaxProfileResponse>> SaveProfileAsync(
        SaveTaxProfileRequest req, CancellationToken ct = default)
    {
        if (!Enum.TryParse<TaxRegime>(req.Regime, ignoreCase: true, out var regime))
            return Error.Validation($"Невідома форма оподаткування: {req.Regime}.");
        if (!Enum.TryParse<ZusType>(req.ZusType, ignoreCase: true, out var zusType))
            return Error.Validation($"Невідомий тип ZUS: {req.ZusType}.");

        // MVP: a single active profile — upsert the first row.
        var p = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (p is null)
        {
            p = new TaxProfile { ValidFrom = new DateOnly(DateTime.Now.Year, 1, 1) };
            db.TaxProfiles.Add(p);
        }

        p.Regime = regime;
        p.RyczaltRate = req.RyczaltRate;
        p.VatPayer = req.VatPayer;
        p.VatRate = req.VatRate;
        p.ZusType = zusType;
        p.ZusSocial = req.ZusSocial;
        p.HealthContribution = req.HealthContribution;
        p.Chorobowe = req.Chorobowe;
        p.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<TaxProfileResponse>.Ok(ToResponse(p));
    }

    /// Live feedback while typing an invoice: what it adds to THIS MONTH's budget.
    /// The delta is taxed(revenue so far + this invoice) - taxed(revenue so far), so ZUS and
    /// health are never charged twice — the second invoice of a month legitimately adds more
    /// take-home than the first. Matches what the home screen will show after saving.
    public async Task<Result<IncomePreviewResponse>> PreviewIncomeAsync(
        CalculateTakeHomeRequest req, CancellationToken ct = default)
    {
        var p = await LoadOrDefaultAsync(ct);

        var invoice = TakeHomeCalculator.Calculate(p, req.Amount, req.AmountIncludesVat);
        if (!invoice.IsSuccess) return invoice.Error;

        var (first, last) = MonthRange.Of(DateOnly.FromDateTime(DateTime.Now));
        var revenueSoFar = await db.Transactions
            .Where(t => t.Date >= first && t.Date <= last && t.Kind == TransactionKind.Income)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var after = TakeHomeCalculator.Calculate(p, revenueSoFar + invoice.Value!.Revenue, amountIncludesVat: false);
        if (!after.IsSuccess) return after.Error;

        // No income yet = no budget from income yet, so the whole take-home is the delta.
        var budgetBefore = 0m;
        if (revenueSoFar > 0)
        {
            var before = TakeHomeCalculator.Calculate(p, revenueSoFar, amountIncludesVat: false);
            if (!before.IsSuccess) return before.Error;
            budgetBefore = before.Value!.TakeHome;
        }

        // The savings goal follows the budget, so it has to be computed against the budget
        // this invoice produces — not the one on screen a second ago.
        var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var goalAfter = SavingsCalculator.MonthGoal(plan, after.Value!.TakeHome);

        var b = invoice.Value!;
        return Result<IncomePreviewResponse>.Ok(new IncomePreviewResponse(
            b.GrossWithVat, b.VatAmount, b.Revenue,
            budgetBefore, after.Value!.TakeHome, after.Value!.TakeHome - budgetBefore,
            revenueSoFar == 0m, after.Value!.ToMonthBreakdown(),
            (plan?.Mode ?? SavingsMode.Percent).ToString(), plan?.Value ?? 0m, plan?.Active ?? false,
            goalAfter, Money.BaseCurrency));
    }

    public TaxDefaultsResponse GetDefaults() => new(
        PolishTaxDefaults2026.Year,
        PolishTaxDefaults2026.DuzyWithChorobowe,
        PolishTaxDefaults2026.DuzyWithoutChorobowe,
        PolishTaxDefaults2026.PreferencyjnyWithChorobowe,
        PolishTaxDefaults2026.PreferencyjnyWithoutChorobowe,
        PolishTaxDefaults2026.HealthRyczaltUnder60k,
        PolishTaxDefaults2026.HealthRyczalt60kTo300k,
        PolishTaxDefaults2026.HealthRyczaltOver300k);

    /// Returns the saved profile, or an unsaved one prefilled with current-year suggestions.
    private async Task<TaxProfile> LoadOrDefaultAsync(CancellationToken ct)
    {
        var p = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return p ?? new TaxProfile
        {
            ZusSocial = PolishTaxDefaults2026.SuggestZusSocial(ZusType.Duzy, chorobowe: false),
            HealthContribution = PolishTaxDefaults2026.HealthRyczalt60kTo300k,
            ValidFrom = new DateOnly(PolishTaxDefaults2026.Year, 1, 1),
        };
    }

    private static TaxProfileResponse ToResponse(TaxProfile p) => new(
        p.Regime.ToString(), p.RyczaltRate, p.VatPayer, p.VatRate, p.ZusType.ToString(),
        p.ZusSocial, p.HealthContribution, p.Chorobowe, p.ValidFrom,
        p.ZusSocial + p.HealthContribution);
}
