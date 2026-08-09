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
using Microsoft.Extensions.Logging;

namespace FinanceApp.Application.Savings;

public interface ISavingsService
{
    Task<SavingsResponse> GetAsync(CancellationToken ct = default);
    Task<Result<SavingsResponse>> SavePlanAsync(SaveSavingsPlanRequest req, CancellationToken ct = default);
    Task<Result<SavingsResponse>> AddEntryAsync(SaveSavingsEntryRequest req, CancellationToken ct = default);
    Task<Result<SavingsResponse>> UpdateEntryAsync(int id, SaveSavingsEntryRequest req, CancellationToken ct = default);
    Task<Result<SavingsResponse>> DeleteEntryAsync(int id, CancellationToken ct = default);

    /// Moving money from one jar to another. Two movements by hand did the job — a withdrawal
    /// here, a deposit there — but between them the money existed nowhere, and if the second
    /// one was forgotten it stayed nowhere.
    Task<Result<SavingsResponse>> TransferAsync(TransferRequest req, CancellationToken ct = default);
}

public sealed class SavingsService(
    IAppDbContext db, IMonthlyBudget monthlyBudget, IFxConverter fx,
    IAllocationService allocations, IEnvelopeService envelopes,
    IMoneyViewFactory moneyViews, ILogger<SavingsService> log) : ISavingsService
{
    public async Task<SavingsResponse> GetAsync(CancellationToken ct = default) =>
        await BuildAsync(await MonthAsync(ct), ct);

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
        return Result<SavingsResponse>.Ok(await BuildAsync(await MonthAsync(ct), ct));
    }

    public async Task<Result<SavingsResponse>> AddEntryAsync(
        SaveSavingsEntryRequest req, CancellationToken ct = default)
    {
        var entry = new SavingsEntry { CreatedAt = DateTimeOffset.UtcNow };
        var applied = await ApplyAsync(entry, req, replacing: 0m, ct);
        if (!applied.IsSuccess) return applied.Error;

        db.SavingsEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Result<SavingsResponse>.Ok(await BuildAsync(await MonthAsync(ct), ct));
    }

    public async Task<Result<SavingsResponse>> UpdateEntryAsync(
        int id, SaveSavingsEntryRequest req, CancellationToken ct = default)
    {
        var entry = await db.SavingsEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null)
        {
            log.LogWarning("Savings: entry {Id} was edited but no longer exists", id);
            return Error.NotFound($"Операцію {id} не знайдено.");
        }

        // Half a transfer is not a movement of its own: correcting one side would leave the
        // other saying something else about the same act. Delete it and move the money again.
        if (entry.TransferKey is not null)
            return Error.Validation("Це перекидання між банками — його можна лише скасувати цілком.");

        // The row being edited is already part of the balance, so it has to be taken out
        // before checking the new amount — otherwise correcting a withdrawal down would
        // be rejected by the balance it itself produced.
        var replacing = entry.Kind == SavingsEntryKind.Deposit ? entry.AmountBase : -entry.AmountBase;

        var applied = await ApplyAsync(entry, req, replacing, ct);
        if (!applied.IsSuccess) return applied.Error;

        await db.SaveChangesAsync(ct);
        return Result<SavingsResponse>.Ok(await BuildAsync(await MonthAsync(ct), ct));
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
        // A put-away envelope is not a destination: it was emptied on purpose, and money
        // arriving in it would be money the list no longer shows.
        if (!await db.Envelopes.AnyAsync(e => e.Id == envelopeId && e.ArchivedAt == null, ct))
            return Error.NotFound($"Банку {envelopeId} не знайдено.");

        var conv = await fx.ConvertToBaseAsync(req.Amount, currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        // Checked against the jar's whole balance, which counts the two ways money leaves it
        // without being a withdrawal: an expense paid straight out of it, and a debt repaid
        // from it. This used to count deposits minus withdrawals here, and a jar with 1 000
        // put in and 800 already spent from it let another 500 be taken out — then showed the
        // balance it really had, in minus.
        //
        // Only what this same entry already contributes to THIS envelope can be replaced;
        // moving an entry to another pot has to earn its room in the new one.
        var available = await envelopes.BalanceAsync(envelopeId, ct)
            - (entry.EnvelopeId == envelopeId ? replacing : 0m);
        if (kind == SavingsEntryKind.Withdrawal && conv.Value!.AmountBase > available)
            return Error.Validation($"У банці лише {available:0.00}. Стільки зняти не вийде.");

        entry.EnvelopeId = envelopeId;
        entry.Date = date;
        entry.Kind = kind;
        entry.AmountOriginal = req.Amount;
        entry.CurrencyOriginal = currency;
        entry.AmountBase = conv.Value!.AmountBase;
        entry.FxRate = conv.Value.Rate;
        entry.FxDate = conv.Value.RateDate;
        entry.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        // Only a deposit can be money that was already put away. A withdrawal takes money out
        // of the jar and back into spendable, which is a movement whenever it happened.
        entry.AlreadySetAside = kind == SavingsEntryKind.Deposit && req.AlreadySetAside;

        return Result<SavingsEntry>.Ok(entry);
    }

    public async Task<Result<SavingsResponse>> TransferAsync(
        TransferRequest req, CancellationToken ct = default)
    {
        if (req.Amount <= 0) return Error.Validation("Сума має бути більшою за нуль.");
        if (req.FromEnvelopeId == req.ToEnvelopeId)
            return Error.Validation("Це та сама банка — перекидати нема куди.");

        var jars = await db.Envelopes
            .Where(e => (e.Id == req.FromEnvelopeId || e.Id == req.ToEnvelopeId) && e.ArchivedAt == null)
            .ToListAsync(ct);
        var from = jars.FirstOrDefault(e => e.Id == req.FromEnvelopeId);
        var to = jars.FirstOrDefault(e => e.Id == req.ToEnvelopeId);
        if (from is null) return Error.NotFound($"Банку {req.FromEnvelopeId} не знайдено.");
        if (to is null) return Error.NotFound($"Банку {req.ToEnvelopeId} не знайдено.");

        var date = req.Date ?? DateOnly.FromDateTime(DateTime.Now);
        var currency = string.IsNullOrWhiteSpace(req.Currency)
            ? Money.BaseCurrency
            : req.Currency.ToUpperInvariant();

        var conv = await fx.ConvertToBaseAsync(req.Amount, currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        var available = await envelopes.BalanceAsync(from.Id, ct);
        if (conv.Value!.AmountBase > available)
            return Error.Validation($"У банці «{from.Name}» лише {available:0.00}. Стільки перекинути не вийде.");

        // One key on both halves: they are one act, and DeleteEntryAsync undoes them together.
        var key = Guid.NewGuid().ToString();
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

        db.SavingsEntries.Add(Half(from.Id, SavingsEntryKind.Withdrawal, note ?? $"У «{to.Name}»"));
        db.SavingsEntries.Add(Half(to.Id, SavingsEntryKind.Deposit, note ?? $"З «{from.Name}»"));

        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Savings: moved {Amount} from «{From}» to «{To}»", conv.Value.AmountBase, from.Name, to.Name);
        return Result<SavingsResponse>.Ok(await BuildAsync(await MonthAsync(ct), ct));

        SavingsEntry Half(int envelopeId, SavingsEntryKind kind, string note) => new()
        {
            EnvelopeId = envelopeId,
            Date = date,
            Kind = kind,
            AmountOriginal = req.Amount,
            CurrencyOriginal = currency,
            AmountBase = conv.Value!.AmountBase,
            FxRate = conv.Value.Rate,
            FxDate = conv.Value.RateDate,
            TransferKey = key,
            Note = note,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public async Task<Result<SavingsResponse>> DeleteEntryAsync(int id, CancellationToken ct = default)
    {
        var entry = await db.SavingsEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entry is null)
        {
            log.LogWarning("Savings: entry {Id} was deleted but no longer exists", id);
            return Error.NotFound($"Операцію {id} не знайдено.");
        }

        // A transfer goes as a whole. Removing one half would leave money that left a jar and
        // arrived nowhere — or arrived from nowhere — and «Відкладено всього» would say so.
        if (entry.TransferKey is { } key)
        {
            var both = await db.SavingsEntries.Where(x => x.TransferKey == key).ToListAsync(ct);
            db.SavingsEntries.RemoveRange(both);
            await db.SaveChangesAsync(ct);
            return Result<SavingsResponse>.Ok(await BuildAsync(await MonthAsync(ct), ct));
        }

        db.SavingsEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return Result<SavingsResponse>.Ok(await BuildAsync(await MonthAsync(ct), ct));
    }

    /// The default envelope, which is what the plan on this screen feeds.
    private async Task<EnvelopeStatus> DefaultStatusAsync(MonthlyBudgetResult month, CancellationToken ct)
    {
        var all = await envelopes.StatusAsync(month, ct);
        return all.FirstOrDefault(e => e.IsDefault) ?? all[0];
    }

    private async Task<SavingsResponse> BuildAsync(MonthlyBudgetResult month, CancellationToken ct)
    {
        var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var all = await envelopes.StatusAsync(month, ct);
        var status = all.FirstOrDefault(e => e.IsDefault) ?? all[0];

        // A scheme bucket named like the default envelope owns its goal, and then the plan's
        // own value is ignored — the screen has to say so rather than show a number that does
        // nothing.
        var breakdown = await allocations.BreakdownAsync(month.Budget ?? 0m, ct);
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
                x.EnvelopeId, x.Envelope?.Name ?? "", x.IsAuto, x.TransferKey is not null,
                x.AlreadySetAside));

        var summaries = new List<EnvelopeSummary>(all.Count);
        foreach (var e in all)
            summaries.Add(new EnvelopeSummary(
                e.Id, e.Name, e.Kind.ToString(), e.IsDefault,
                await view.FromBaseTodayAsync(e.Balance, ct),
                await view.FromBaseTodayAsync(e.MonthGoal, ct),
                await view.FromBaseTodayAsync(e.DepositedThisMonth, ct),
                await view.FromBaseTodayAsync(e.StillToReserve, ct),
                e.IsFromScheme,
                await TargetViewAsync(e.Target, view, ct)));

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
            fromScheme,
            month.FromOpeningBalance ? month.WindowStart : null);
    }

    /// The target read out in the currency the user is reading in — same treatment as the
    /// balance beside it, because a goal shown in another currency than the money in the jar
    /// could not be compared with it.
    private static async Task<EnvelopeTargetResponse?> TargetViewAsync(
        EnvelopeTargetStatus? target, MoneyView view, CancellationToken ct)
    {
        if (target is null) return null;

        return new EnvelopeTargetResponse(
            await view.FromBaseTodayAsync(target.Amount, ct),
            target.Date,
            await view.FromBaseTodayAsync(target.Remaining, ct),
            target.PeriodsLeft,
            await view.FromBaseTodayAsync(target.PerPeriod, ct),
            target.Reached,
            target.Overdue);
    }

    /// A percentage goal is a share of what is actually the user's after tax — and the plan
    /// only runs at all when the period was not started by counting a balance, so the whole
    /// resolution travels together rather than just the amount.
    private Task<MonthlyBudgetResult> MonthAsync(CancellationToken ct) =>
        monthlyBudget.ResolveAsync(ct);

    /// Where money goes when nobody said. StatusAsync creates the envelopes if they are
    /// missing, so this can never come back empty.
    private async Task<int> DefaultEnvelopeIdAsync(CancellationToken ct) =>
        (await DefaultStatusAsync(await MonthAsync(ct), ct)).Id;

}
