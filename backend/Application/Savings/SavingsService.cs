using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
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

public sealed class SavingsService(
    IAppDbContext db, IMonthlyBudget monthlyBudget, IFxConverter fx,
    IAllocationService allocations, IEnvelopeService envelopes,
    IMoneyViewFactory moneyViews) : ISavingsService
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

        // An entry keeps its envelope when the request does not name one, so editing a
        // pension deposit cannot silently move it into savings.
        var envelopeId = req.EnvelopeId ?? (entry.EnvelopeId != 0 ? entry.EnvelopeId : await DefaultEnvelopeIdAsync(ct));
        if (!await db.Envelopes.AnyAsync(e => e.Id == envelopeId, ct))
            return Error.NotFound($"Конверт {envelopeId} не знайдено.");

        var conv = await fx.ConvertToBaseAsync(req.Amount, currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        // Only what this same entry already contributes to THIS envelope can be replaced;
        // moving an entry to another pot has to earn its room in the new one.
        var available = await BalanceAsync(envelopeId, ct)
            - (entry.EnvelopeId == envelopeId ? replacing : 0m);
        if (kind == SavingsEntryKind.Withdrawal && conv.Value!.AmountBase > available)
            return Error.Validation($"У конверті лише {available:0.00}. Стільки зняти не вийде.");

        entry.EnvelopeId = envelopeId;
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
        var e = await DefaultStatusAsync(monthlyTakeHome, ct);
        return new SavingsStatus(e.Balance, e.MonthGoal, e.DepositedThisMonth, e.StillToReserve);
    }

    /// The default envelope, which is what the plan on this screen feeds.
    private async Task<EnvelopeStatus> DefaultStatusAsync(decimal monthlyTakeHome, CancellationToken ct)
    {
        var all = await envelopes.StatusAsync(monthlyTakeHome, ct);
        return all.FirstOrDefault(e => e.IsDefault) ?? all[0];
    }

    private async Task<SavingsResponse> BuildAsync(decimal monthlyTakeHome, CancellationToken ct)
    {
        var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var all = await envelopes.StatusAsync(monthlyTakeHome, ct);
        var status = all.FirstOrDefault(e => e.IsDefault) ?? all[0];

        // A scheme bucket named like the default envelope owns its goal, and then the plan's
        // own value is ignored — the screen has to say so rather than show a number that does
        // nothing.
        var breakdown = await allocations.BreakdownAsync(monthlyTakeHome, ct);
        var fromScheme = breakdown.SavingsGoal is null ? null : breakdown.SchemeName;

        var rows = await db.SavingsEntries
            .Include(x => x.Envelope)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Take(100)
            .ToListAsync(ct);

        var view = await moneyViews.CurrentAsync(ct);

        // Movements are history: each is read at its own date. The balance and the goal are
        // about now, so they take today's rate — the same split the month summary makes.
        var entries = new List<SavingsEntryResponse>(rows.Count);
        foreach (var x in rows)
            entries.Add(new SavingsEntryResponse(
                x.Id, x.Date, x.Kind.ToString(), await view.FromBaseAsync(x.AmountBase, x.Date, ct),
                x.AmountOriginal, x.CurrencyOriginal, x.Note,
                x.EnvelopeId, x.Envelope?.Name ?? ""));

        var summaries = new List<EnvelopeSummary>(all.Count);
        foreach (var e in all)
            summaries.Add(new EnvelopeSummary(
                e.Id, e.Name, e.Kind.ToString(), e.IsDefault,
                await view.FromBaseTodayAsync(e.Balance, ct),
                await view.FromBaseTodayAsync(e.MonthGoal, ct),
                await view.FromBaseTodayAsync(e.DepositedThisMonth, ct),
                await view.FromBaseTodayAsync(e.StillToReserve, ct)));

        return new SavingsResponse(
            plan?.Mode.ToString() ?? SavingsMode.Fixed.ToString(),
            plan?.Value ?? 0m,
            plan?.Active ?? false,
            await view.FromBaseTodayAsync(status.Balance, ct),
            await view.FromBaseTodayAsync(status.MonthGoal, ct),
            await view.FromBaseTodayAsync(status.DepositedThisMonth, ct),
            await view.FromBaseTodayAsync(status.StillToReserve, ct),
            view.Currency,
            entries,
            summaries,
            fromScheme);
    }

    /// A percentage goal is a share of what is actually the user's after tax.
    private async Task<decimal> TakeHomeAsync(CancellationToken ct) =>
        (await monthlyBudget.ResolveAsync(ct)).Budget ?? 0m;

    /// Where money goes when nobody said. StatusAsync creates the envelopes if they are
    /// missing, so this can never come back empty.
    private async Task<int> DefaultEnvelopeIdAsync(CancellationToken ct) =>
        (await DefaultStatusAsync(await TakeHomeAsync(ct), ct)).Id;

    /// Balance of ONE envelope. Withdrawing is checked against the pot the money is being
    /// taken from, never against the total — otherwise a full pension envelope would let
    /// someone empty a savings envelope that has nothing in it.
    private async Task<decimal> BalanceAsync(int envelopeId, CancellationToken ct)
    {
        var rows = db.SavingsEntries.Where(x => x.EnvelopeId == envelopeId);

        var deposits = await rows
            .Where(x => x.Kind == SavingsEntryKind.Deposit)
            .SumAsync(x => (decimal?)x.AmountBase, ct) ?? 0m;
        var withdrawals = await rows
            .Where(x => x.Kind == SavingsEntryKind.Withdrawal)
            .SumAsync(x => (decimal?)x.AmountBase, ct) ?? 0m;

        return deposits - withdrawals;
    }
}
