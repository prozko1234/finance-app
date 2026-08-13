using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Envelopes;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Debts;
using FinanceApp.Domain.Fx;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceApp.Application.Debts;

public interface IDebtService
{
    Task<DebtsResponse> GetAsync(CancellationToken ct = default);
    Task<Result<DebtsResponse>> AddAsync(SaveDebtRequest req, CancellationToken ct = default);
    Task<Result<DebtsResponse>> UpdateAsync(int id, SaveDebtRequest req, CancellationToken ct = default);
    Task<Result<DebtsResponse>> DeleteAsync(int id, CancellationToken ct = default);

    /// Calling a debt finished, whatever the arithmetic says. Debts get forgiven and rounded
    /// off, and an app that only closed them when the sums came out even would leave a list
    /// of settled business nobody can clear.
    Task<Result<DebtsResponse>> SetClosedAsync(int id, bool closed, CancellationToken ct = default);

    Task<Result<DebtsResponse>> AddPaymentAsync(
        int debtId, SaveDebtPaymentRequest req, CancellationToken ct = default);
    Task<Result<DebtsResponse>> DeletePaymentAsync(int paymentId, CancellationToken ct = default);
}

public sealed class DebtService(
    IAppDbContext db, IFxConverter fx, IBudgetPeriods periods, IDebtLedger ledger,
    IEnvelopeService envelopes, IMoneyViewFactory moneyViews, ILogger<DebtService> log) : IDebtService
{
    public async Task<DebtsResponse> GetAsync(CancellationToken ct = default) => await BuildAsync(ct);

    public async Task<Result<DebtsResponse>> AddAsync(
        SaveDebtRequest req, CancellationToken ct = default)
    {
        var debt = new Debt { CreatedAt = DateTimeOffset.UtcNow };
        var applied = await ApplyAsync(debt, req, ct);
        if (!applied.IsSuccess) return applied.Error;

        db.Debts.Add(debt);
        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Debts: {Direction} {Amount} {Currency} with «{Person}»",
            debt.Direction, debt.AmountOriginal, debt.CurrencyOriginal, debt.Person);
        return Result<DebtsResponse>.Ok(await BuildAsync(ct));
    }

    public async Task<Result<DebtsResponse>> UpdateAsync(
        int id, SaveDebtRequest req, CancellationToken ct = default)
    {
        var debt = await db.Debts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (debt is null) return Error.NotFound($"Борг {id} не знайдено.");

        var paid = await PaidAsync(id, ct);
        var applied = await ApplyAsync(debt, req, ct);
        if (!applied.IsSuccess) return applied.Error;

        // The debt cannot be corrected down past what has already gone against it: the
        // remainder would be negative, and a debt that owes the user money back is not a
        // thing this screen can say. Deleting the payments is how that is undone.
        if (debt.AmountBase < paid)
            return Error.Validation(
                $"За цим боргом уже пройшло {paid:0.00}. Менше цієї суми поставити не вийде — " +
                "спершу прибери зайві платежі.");

        await db.SaveChangesAsync(ct);
        return Result<DebtsResponse>.Ok(await BuildAsync(ct));
    }

    public async Task<Result<DebtsResponse>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var debt = await db.Debts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (debt is null) return Error.NotFound($"Борг {id} не знайдено.");

        // Payments go with it — the database cascades them. They are movements against this
        // debt and mean nothing without it; left behind, they would go on being counted as
        // spending on something that no longer exists.
        db.Debts.Remove(debt);
        await db.SaveChangesAsync(ct);
        return Result<DebtsResponse>.Ok(await BuildAsync(ct));
    }

    public async Task<Result<DebtsResponse>> SetClosedAsync(
        int id, bool closed, CancellationToken ct = default)
    {
        var debt = await db.Debts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (debt is null) return Error.NotFound($"Борг {id} не знайдено.");

        debt.ClosedOn = closed ? DateOnly.FromDateTime(DateTime.Now) : null;
        await db.SaveChangesAsync(ct);
        return Result<DebtsResponse>.Ok(await BuildAsync(ct));
    }

    public async Task<Result<DebtsResponse>> AddPaymentAsync(
        int debtId, SaveDebtPaymentRequest req, CancellationToken ct = default)
    {
        var debt = await db.Debts.FirstOrDefaultAsync(d => d.Id == debtId, ct);
        if (debt is null) return Error.NotFound($"Борг {debtId} не знайдено.");

        if (!Enum.TryParse<MoneySource>(req.Source, ignoreCase: true, out var source))
            return Error.Validation($"Невідоме джерело платежу: {req.Source}.");
        if (req.Amount <= 0) return Error.Validation("Сума має бути більшою за нуль.");

        // Money coming BACK cannot come out of a jar: it is arriving, not leaving. Letting it
        // name an envelope would quietly make the pot smaller for money that went into it.
        if (debt.Direction == DebtDirection.TheyOweMe && source == MoneySource.Envelope)
            return Error.Validation("Гроші повертають тобі — з банки їх не беруть.");

        var date = req.Date ?? DateOnly.FromDateTime(DateTime.Now);
        var currency = string.IsNullOrWhiteSpace(req.Currency)
            ? Money.BaseCurrency
            : req.Currency.ToUpperInvariant();

        var conv = await fx.ConvertToBaseAsync(req.Amount, currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        var outstanding = debt.AmountBase - await PaidAsync(debtId, ct);
        if (conv.Value!.AmountBase > outstanding)
            return Error.Validation(
                $"Лишилось {outstanding:0.00}. Більше за борг провести не вийде — " +
                "якщо сума боргу неправильна, виправ її.");

        int? envelopeId = null;
        if (source == MoneySource.Envelope)
        {
            if (req.EnvelopeId is not { } wanted)
                return Error.Validation("Не сказано, з якої банки платити.");
            if (!await db.Envelopes.AnyAsync(e => e.Id == wanted && e.ArchivedAt == null, ct))
                return Error.NotFound($"Банку {wanted} не знайдено.");

            // Checked against the jar's real balance — deposits and withdrawals less anything
            // already spent or repaid out of it. A jar that could go negative would hold back
            // money from the daily norm that is not there.
            var balance = await envelopes.BalanceAsync(wanted, ct);
            if (conv.Value.AmountBase > balance)
                return Error.Validation($"У банці лише {balance:0.00}. Стільки взяти не вийде.");

            envelopeId = wanted;
        }

        db.DebtPayments.Add(new DebtPayment
        {
            DebtId = debtId,
            Date = date,
            AmountOriginal = req.Amount,
            CurrencyOriginal = currency,
            AmountBase = conv.Value.AmountBase,
            FxRate = conv.Value.Rate,
            FxDate = conv.Value.RateDate,
            Source = source,
            EnvelopeId = envelopeId,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Debts: {Amount} against debt {Id} with «{Person}», from {Source}",
            conv.Value.AmountBase, debtId, debt.Person, source);
        return Result<DebtsResponse>.Ok(await BuildAsync(ct));
    }

    public async Task<Result<DebtsResponse>> DeletePaymentAsync(
        int paymentId, CancellationToken ct = default)
    {
        var payment = await db.DebtPayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return Error.NotFound($"Платіж {paymentId} не знайдено.");

        db.DebtPayments.Remove(payment);
        await db.SaveChangesAsync(ct);
        return Result<DebtsResponse>.Ok(await BuildAsync(ct));
    }

    /// Shared by add and edit so both validate identically and convert on the same date —
    /// two code paths writing money is how a total starts to disagree with its parts.
    private async Task<Result<Debt>> ApplyAsync(
        Debt debt, SaveDebtRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<DebtDirection>(req.Direction, ignoreCase: true, out var direction))
            return Error.Validation($"Невідомий напрям боргу: {req.Direction}.");
        if (req.Amount <= 0) return Error.Validation("Сума має бути більшою за нуль.");

        var person = req.Person?.Trim() ?? "";
        if (person.Length == 0) return Error.Validation("Скажи, з ким цей борг.");

        var date = req.Date ?? DateOnly.FromDateTime(DateTime.Now);
        if (req.Deadline is { } deadline && deadline < date)
            return Error.Validation("Дедлайн раніший за сам борг — так не буває.");

        // Reserving needs a deadline to divide by, and only makes sense for money the user has
        // to give back. Refused rather than quietly ignored: a switch that is on and does
        // nothing is worse than one that explains itself.
        if (req.ReserveFromBudget)
        {
            if (direction != DebtDirection.IOwe)
                return Error.Validation(
                    "Відкладати можна на те, що віддаєш ти. Гроші, які винні тобі, відкладати нема з чого.");
            if (req.Deadline is null)
                return Error.Validation("Щоб відкладати щоперіоду, потрібен дедлайн — інакше нема на скільки ділити.");
        }

        var currency = string.IsNullOrWhiteSpace(req.Currency)
            ? Money.BaseCurrency
            : req.Currency.ToUpperInvariant();

        var conv = await fx.ConvertToBaseAsync(req.Amount, currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        var origin = await OriginAsync(debt, direction, req, conv.Value!.AmountBase, ct);
        if (!origin.IsSuccess) return origin.Error;

        debt.Direction = direction;
        debt.Origin = origin.Value!.Source;
        debt.OriginEnvelopeId = origin.Value.EnvelopeId;
        debt.Person = person;
        debt.Date = date;
        debt.Deadline = req.Deadline;
        debt.ReserveFromBudget = req.ReserveFromBudget;
        debt.AmountOriginal = req.Amount;
        debt.CurrencyOriginal = currency;
        debt.AmountBase = conv.Value!.AmountBase;
        debt.FxRate = conv.Value.Rate;
        debt.FxDate = conv.Value.RateDate;
        debt.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();

        return Result<Debt>.Ok(debt);
    }

    /// Where the money for a debt came from, checked. Split out of <see cref="ApplyAsync"/>
    /// only because it is the one part with real rules behind it.
    ///
    /// The rules mirror a payment's, one direction round: money the user HANDS OVER can come
    /// out of a jar, money that ARRIVES cannot go into one. Borrowing 500 zł and putting it
    /// straight in the holiday jar is a real thing to do, but it is two movements — take the
    /// loan, then deposit — and letting one form do both would make the jar's history a place
    /// where money appears without a deposit behind it.
    private async Task<Result<(MoneySource Source, int? EnvelopeId)>> OriginAsync(
        Debt debt, DebtDirection direction, SaveDebtRequest req, decimal amountBase,
        CancellationToken ct)
    {
        // Null reads as "the money moved before the app was told". That is what every debt
        // written down before this field existed means, and it is the only default that cannot
        // move money behind the user's back.
        if (string.IsNullOrWhiteSpace(req.Origin))
            return Result<(MoneySource, int?)>.Ok((MoneySource.AlreadyHappened, null));

        if (!Enum.TryParse<MoneySource>(req.Origin, ignoreCase: true, out var source))
            return Error.Validation($"Невідоме джерело грошей: {req.Origin}.");

        if (source != MoneySource.Envelope)
            return Result<(MoneySource, int?)>.Ok((source, null));

        if (direction == DebtDirection.IOwe)
            return Error.Validation(
                "Ці гроші приходять тобі — в банку вони так не потраплять. Поклади їх у банку окремо.");

        if (req.OriginEnvelopeId is not { } wanted)
            return Error.Validation("Не сказано, з якої банки ці гроші.");
        if (!await db.Envelopes.AnyAsync(e => e.Id == wanted && e.ArchivedAt == null, ct))
            return Error.NotFound($"Банку {wanted} не знайдено.");

        // The jar's balance already has this debt's own draw taken out of it when the debt is
        // being edited rather than created, so it goes back in before the check — otherwise
        // correcting «позичив 500» to «позичив 520» would be measured against a jar that is
        // 500 short of what it really holds.
        var mine = debt.Origin == MoneySource.Envelope && debt.OriginEnvelopeId == wanted
            ? debt.AmountBase
            : 0m;
        var balance = await envelopes.BalanceAsync(wanted, ct) + mine;
        if (amountBase > balance)
            return Error.Validation($"У банці лише {balance:0.00}. Стільки позичити не вийде.");

        return Result<(MoneySource, int?)>.Ok((source, wanted));
    }

    private async Task<decimal> PaidAsync(int debtId, CancellationToken ct) =>
        await db.DebtPayments
            .Where(p => p.DebtId == debtId)
            .SumAsync(p => (decimal?)p.AmountBase, ct) ?? 0m;

    private async Task<DebtsResponse> BuildAsync(CancellationToken ct)
    {
        var debts = await db.Debts
            .Include(d => d.OriginEnvelope)
            .OrderBy(d => d.ClosedOn != null)
            .ThenBy(d => d.Deadline == null)
            .ThenBy(d => d.Deadline)
            .ThenByDescending(d => d.Date)
            .ToListAsync(ct);

        var payments = await db.DebtPayments
            .Include(p => p.Envelope)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .ToListAsync(ct);

        var period = await periods.CurrentAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var view = await moneyViews.CurrentAsync(ct);
        var reserve = await ledger.ReserveByDebtAsync(period, ct);

        var rows = new List<DebtResponse>(debts.Count);

        foreach (var debt in debts)
        {
            var mine = payments.Where(p => p.DebtId == debt.Id).ToList();
            var paid = mine.Sum(p => p.AmountBase);
            var outstanding = Math.Max(0m, debt.AmountBase - paid);

            var periodsLeft = await periods.CountUntilAsync(debt.Deadline, period, ct) ?? 0;

            var payViews = new List<DebtPaymentResponse>(mine.Count);
            foreach (var p in mine)
                payViews.Add(new DebtPaymentResponse(
                    p.Id, p.Date,
                    // Each movement is read at its own date: it is history, and the rate it
                    // happened at is the true one. The totals below are about now.
                    await view.FromBaseAsync(p.AmountBase, p.Date, ct),
                    p.AmountOriginal, p.CurrencyOriginal, p.Source.ToString(),
                    p.EnvelopeId, p.Envelope?.Name, p.Note));

            rows.Add(new DebtResponse(
                debt.Id, debt.Direction.ToString(), debt.Person,
                await view.FromBaseTodayAsync(debt.AmountBase, ct),
                debt.AmountOriginal, debt.CurrencyOriginal,
                debt.Date, debt.Deadline, debt.ReserveFromBudget,
                await view.FromBaseTodayAsync(paid, ct),
                await view.FromBaseTodayAsync(outstanding, ct),
                // Read from the ledger rather than worked out again here, so this figure and
                // the one missing from the daily norm are the same figure.
                await view.FromBaseTodayAsync(reserve.GetValueOrDefault(debt.Id), ct),
                periodsLeft,
                // Overdue is about the debt, not about the pace: a debt with nothing left to
                // pay is settled whether or not its date has gone by.
                outstanding > 0 && debt.ClosedOn is null && debt.Deadline is { } due && due < today,
                debt.ClosedOn, debt.Note, payViews,
                debt.Origin.ToString(), debt.OriginEnvelopeId, debt.OriginEnvelope?.Name));
        }

        var open = rows.Where(r => r.ClosedOn is null).ToList();

        return new DebtsResponse(
            view.Currency,
            open.Where(r => r.Direction == nameof(DebtDirection.IOwe)).Sum(r => r.Outstanding),
            open.Where(r => r.Direction == nameof(DebtDirection.TheyOweMe)).Sum(r => r.Outstanding),
            await view.FromBaseTodayAsync(reserve.Values.Sum(), ct),
            rows.Where(r => r.Direction == nameof(DebtDirection.IOwe)).ToList(),
            rows.Where(r => r.Direction == nameof(DebtDirection.TheyOweMe)).ToList());
    }
}
