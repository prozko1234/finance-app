using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Tax;

public interface ITaxService
{
    Task<TaxProfileResponse> GetProfileAsync(CancellationToken ct = default);
    Task<Result<TaxProfileResponse>> SaveProfileAsync(SaveTaxProfileRequest req, CancellationToken ct = default);
    Task<Result<TakeHomeResponse>> CalculateAsync(CalculateTakeHomeRequest req, CancellationToken ct = default);
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

    public async Task<Result<TakeHomeResponse>> CalculateAsync(
        CalculateTakeHomeRequest req, CancellationToken ct = default)
    {
        var p = await LoadOrDefaultAsync(ct);
        var r = TakeHomeCalculator.Calculate(p, req.Amount, req.AmountIncludesVat);
        if (!r.IsSuccess) return r.Error;

        var b = r.Value!;
        return Result<TakeHomeResponse>.Ok(new TakeHomeResponse(
            b.GrossWithVat, b.VatAmount, b.Revenue, b.ZusSocial, b.HealthContribution,
            b.HealthDeducted, b.TaxBase, b.Tax, b.TakeHome, Money.BaseCurrency));
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
