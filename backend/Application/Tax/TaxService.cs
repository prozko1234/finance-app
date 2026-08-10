using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Savings;
using FinanceApp.Application.Allocations;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Tax;

public interface ITaxService
{
    Task<TaxProfileResponse> GetProfileAsync(CancellationToken ct = default);
    Task<Result<TaxProfileResponse>> SaveProfileAsync(SaveTaxProfileRequest req, CancellationToken ct = default);
    Task<Result<IncomePreviewResponse>> PreviewIncomeAsync(CalculateTakeHomeRequest req, CancellationToken ct = default);
    TaxDefaultsResponse GetDefaults();

    /// What the bookkeeper said for a month, beside what the engine makes of it.
    Task<TaxActualsResponse> GetActualsAsync(DateOnly month, CancellationToken ct = default);

    /// Saving all three as null clears the month back to the engine's own figures.
    Task<Result<TaxActualsResponse>> SaveActualsAsync(
        SaveTaxActualsRequest req, CancellationToken ct = default);
}

public sealed class TaxService(
    IAppDbContext db, IBudgetPeriods periods, IAllocationService allocations) : ITaxService
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
        p.StudentUnder26 = req.StudentUnder26;
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

        var (first, last) = await periods.CurrentAsync(ct);
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
        //
        // And it comes from the SAME source as the rest of the app: a scheme bucket owns the
        // goal when there is one, and only then does the plan decide. This used to ask the
        // plan alone, so a scheme with a savings bucket made the form promise a number the
        // app was never going to put aside — and the plan editor inside the form silently
        // did nothing.
        var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var breakdown = await allocations.BreakdownAsync(after.Value!.TakeHome, ct);
        var goalAfter = breakdown.SavingsGoal ?? SavingsCalculator.MonthGoal(plan, after.Value!.TakeHome);
        var fromScheme = breakdown.SavingsGoal is null ? null : breakdown.SchemeName;

        var b = invoice.Value!;
        return Result<IncomePreviewResponse>.Ok(new IncomePreviewResponse(
            b.GrossWithVat, b.VatAmount, b.Revenue,
            budgetBefore, after.Value!.TakeHome, after.Value!.TakeHome - budgetBefore,
            revenueSoFar == 0m, after.Value!.ToMonthBreakdown(),
            (plan?.Mode ?? SavingsMode.Percent).ToString(), plan?.Value ?? 0m, plan?.Active ?? false,
            goalAfter, Money.BaseCurrency, fromScheme));
    }

    public async Task<TaxActualsResponse> GetActualsAsync(DateOnly month, CancellationToken ct = default)
    {
        var first = FirstOf(month);
        var saved = await db.TaxActuals.FirstOrDefaultAsync(x => x.Month == first, ct);
        var computed = await ComputedForAsync(first, ct);

        return new TaxActualsResponse(
            first, saved?.ZusSocial, saved?.Health, saved?.Pit,
            computed.ZusSocial, computed.HealthContribution, computed.Tax, Money.BaseCurrency);
    }

    public async Task<Result<TaxActualsResponse>> SaveActualsAsync(
        SaveTaxActualsRequest req, CancellationToken ct = default)
    {
        if (req.ZusSocial < 0 || req.Health < 0 || req.Pit < 0)
            return Error.Validation("Податок не може бути від'ємним.");

        var first = FirstOf(req.Month);
        var row = await db.TaxActuals.FirstOrDefaultAsync(x => x.Month == first, ct);

        var empty = req.ZusSocial is null && req.Health is null && req.Pit is null;
        if (empty)
        {
            // Clearing every field is how "use the engine's figures again" is expressed, and an
            // all-null override left lying about would be a row that says nothing.
            if (row is not null) db.TaxActuals.Remove(row);
        }
        else
        {
            row ??= new TaxActuals { Month = first };
            row.ZusSocial = req.ZusSocial;
            row.Health = req.Health;
            row.Pit = req.Pit;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            if (row.Id == 0) db.TaxActuals.Add(row);
        }

        await db.SaveChangesAsync(ct);
        return Result<TaxActualsResponse>.Ok(await GetActualsAsync(first, ct));
    }

    /// What the engine makes of the month's own revenue, so the form has something to show the
    /// correction against. Zero all round when nothing came in — there is nothing to owe on it.
    private async Task<TakeHomeBreakdown> ComputedForAsync(DateOnly first, CancellationToken ct)
    {
        var last = first.AddMonths(1).AddDays(-1);

        var revenue = await db.Transactions
            .Where(t => t.Kind == TransactionKind.Income && t.Date >= first && t.Date <= last)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var profile = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (profile is null || revenue <= 0)
            return new TakeHomeBreakdown(revenue, 0m, revenue, 0m, 0m, 0m, revenue, 0m, revenue);

        var take = TakeHomeCalculator.Calculate(profile, revenue, amountIncludesVat: false);
        return take.IsSuccess
            ? take.Value!
            : new TakeHomeBreakdown(revenue, 0m, revenue, 0m, 0m, 0m, revenue, 0m, revenue);
    }

    private static DateOnly FirstOf(DateOnly date) => new(date.Year, date.Month, 1);

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
        p.ZusSocial, p.HealthContribution, p.Chorobowe, p.StudentUnder26, p.ValidFrom,
        // A fixed monthly total only exists on ryczalt; on payroll contributions follow the salary.
        p.Regime == TaxRegime.Ryczalt ? p.ZusSocial + p.HealthContribution : 0m);
}
