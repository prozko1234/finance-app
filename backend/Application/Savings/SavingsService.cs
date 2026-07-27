using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Savings;

public interface ISavingsService
{
    Task<SavingsResponse> GetAsync(CancellationToken ct = default);
    Task<Result<SavingsResponse>> SavePlanAsync(SaveSavingsPlanRequest req, CancellationToken ct = default);
    Task<Result<SavingsResponse>> AddEntryAsync(SaveSavingsEntryRequest req, CancellationToken ct = default);
    Task<Result<SavingsResponse>> UpdateEntryAsync(int id, SaveSavingsEntryRequest req, CancellationToken ct = default);
    Task<Result<SavingsResponse>> DeleteEntryAsync(int id, CancellationToken ct = default);

    /// Balance + this month's goal. Takes take-home because a percentage goal depends on it.
    Task<SavingsStatus> StatusAsync(decimal monthlyTakeHome, CancellationToken ct = default);
}

public sealed class SavingsService(IAppDbContext db, IMonthlyBudget monthlyBudget, IFxConverter fx) : ISavingsService
{
    public async Task<SavingsResponse> GetAsync(CancellationToken ct = default) =>
        await BuildAsync(await TakeHomeAsync(ct), ct);

    public async Task<Result<SavingsResponse>> SavePlanAsync(
        SaveSavingsPlanRequest req, CancellationToken ct = default)
    {
        if (!Enum.TryParse<SavingsMode>(req.Mode, ignoreCase: true, out var mode))
            return Error.Validation($"Невідомий режим відкладання: {req.Mode}.");
        if (req.Value < 0)
            return Error.Validation("Сума не може бути від'ємною.");
        if (mode == SavingsMode.Percent && req.Value > 100)
            return Error.Validation("Відсоток не може бути більшим за 100.");

        var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (plan is null)
        {
            plan = new SavingsPlan();
            db.SavingsPlans.Add(plan);
        }

        plan.Mode = mode;
        plan.Value = req.Value;
        plan.Active = req.Active;
        plan.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<SavingsResponse>.Ok(await BuildAsync(await TakeHomeAsync(ct), ct));
    }

    public async Task<Result<SavingsResponse>> AddEntryAsync(
        SaveSavingsEntryRequest req, CancellationToken ct = default)
    {
        var entry = new SavingsEntry { CreatedAt = DateTimeOffset.UtcNow };
        var applied = await ApplyAsync(entry, req, replacing: 0m, ct);
        if (!applied.IsSuccess) return applied.Error;

        db.SavingsEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Result<SavingsResponse>.Ok(await BuildAsync(await TakeHomeAsync(ct), ct));
    }

    public async Task<Result<SavingsResponse>> UpdateEntryAsync(
        int id, SaveSavingsEntryRequest req, CancellationToken ct = default)
    {
        var entry = await db.SavingsEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null) return Error.NotFound($"Операцію {id} не знайдено.");

        // The row being edited is already part of the balance, so it has to be taken out
        // before checking the new amount — otherwise correcting a withdrawal down would
        // be rejected by the balance it itself produced.
        var replacing = entry.Kind == SavingsEntryKind.Deposit ? entry.AmountBase : -entry.AmountBase;

        var applied = await ApplyAsync(entry, req, replacing, ct);
        if (!applied.IsSuccess) return applied.Error;

        await db.SaveChangesAsync(ct);
        return Result<SavingsResponse>.Ok(await BuildAsync(await TakeHomeAsync(ct), ct));
    }

    /// Shared by add and edit so both validate identically and convert on the same date —
    /// two code paths writing money is how the balance starts to disagree with the entries.
    /// <param name="replacing">Signed base amount this entry already contributes, if any.</param>
    private async Task<Result<SavingsEntry>> ApplyAsync(
        SavingsEntry entry, SaveSavingsEntryRequest req, decimal replacing, CancellationToken ct)
    {
        if (!Enum.TryParse<SavingsEntryKind>(req.Kind, ignoreCase: true, out var kind))
            return Error.Validation($"Невідомий тип операції: {req.Kind}.");
        if (req.Amount <= 0)
            return Error.Validation("Сума має бути більшою за нуль.");

        var date = req.Date ?? entry.Date;
        if (date == default) date = DateOnly.FromDateTime(DateTime.Now);

        var currency = string.IsNullOrWhiteSpace(req.Currency)
            ? Money.BaseCurrency
            : req.Currency.ToUpperInvariant();

        var conv = await fx.ConvertToBaseAsync(req.Amount, currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        var available = await BalanceAsync(ct) - replacing;
        if (kind == SavingsEntryKind.Withdrawal && conv.Value!.AmountBase > available)
            return Error.Validation($"У конверті лише {available:0.00}. Стільки зняти не вийде.");

        entry.Date = date;
        entry.Kind = kind;
        entry.AmountOriginal = req.Amount;
        entry.CurrencyOriginal = currency;
        entry.AmountBase = conv.Value!.AmountBase;
        entry.FxRate = conv.Value.Rate;
        entry.FxDate = conv.Value.RateDate;
        entry.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

        return Result<SavingsEntry>.Ok(entry);
    }

    public async Task<Result<SavingsResponse>> DeleteEntryAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.SavingsEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null) return Error.NotFound($"Операцію {id} не знайдено.");

        db.SavingsEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return Result<SavingsResponse>.Ok(await BuildAsync(await TakeHomeAsync(ct), ct));
    }

    public async Task<SavingsStatus> StatusAsync(decimal monthlyTakeHome, CancellationToken ct = default)
    {
        var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return SavingsCalculator.Status(
            plan, monthlyTakeHome, await BalanceAsync(ct), await DepositedThisMonthAsync(ct));
    }

    private async Task<SavingsResponse> BuildAsync(decimal monthlyTakeHome, CancellationToken ct)
    {
        var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var status = SavingsCalculator.Status(
            plan, monthlyTakeHome, await BalanceAsync(ct), await DepositedThisMonthAsync(ct));

        var entries = await db.SavingsEntries
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Take(100)
            .Select(x => new SavingsEntryResponse(
                x.Id, x.Date, x.Kind.ToString(), x.AmountBase, x.AmountOriginal, x.CurrencyOriginal, x.Note))
            .ToListAsync(ct);

        return new SavingsResponse(
            plan?.Mode.ToString() ?? SavingsMode.Fixed.ToString(),
            plan?.Value ?? 0m,
            plan?.Active ?? false,
            status.Balance,
            status.MonthGoal,
            status.DepositedThisMonth,
            status.StillToReserve,
            Money.BaseCurrency,
            entries);
    }

    /// A percentage goal is a share of what is actually the user's after tax.
    private async Task<decimal> TakeHomeAsync(CancellationToken ct) =>
        (await monthlyBudget.ResolveAsync(ct)).Budget ?? 0m;

    private async Task<decimal> BalanceAsync(CancellationToken ct)
    {
        var deposits = await db.SavingsEntries
            .Where(x => x.Kind == SavingsEntryKind.Deposit)
            .SumAsync(x => (decimal?)x.AmountBase, ct) ?? 0m;
        var withdrawals = await db.SavingsEntries
            .Where(x => x.Kind == SavingsEntryKind.Withdrawal)
            .SumAsync(x => (decimal?)x.AmountBase, ct) ?? 0m;

        return deposits - withdrawals;
    }

    /// Net deposits of the current month — what already left safe-to-spend towards the goal.
    private async Task<decimal> DepositedThisMonthAsync(CancellationToken ct)
    {
        var (first, last) = MonthRange.Of(DateOnly.FromDateTime(DateTime.Now));
        var rows = db.SavingsEntries.Where(x => x.Date >= first && x.Date <= last);

        var deposits = await rows
            .Where(x => x.Kind == SavingsEntryKind.Deposit)
            .SumAsync(x => (decimal?)x.AmountBase, ct) ?? 0m;
        var withdrawals = await rows
            .Where(x => x.Kind == SavingsEntryKind.Withdrawal)
            .SumAsync(x => (decimal?)x.AmountBase, ct) ?? 0m;

        return deposits - withdrawals;
    }
}
