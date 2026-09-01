using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Common;
using FinanceApp.Application.Debts;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using FinanceApp.Application.Summaries;
using FinanceApp.Domain.Savings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceApp.Application.Envelopes;

/// What a jar is being filled up to, and what that means for this period. Amounts are in base
/// currency, like every other figure the service returns.
public record EnvelopeTargetStatus(
    decimal Amount,
    DateOnly? Date,
    decimal Remaining,
    int PeriodsLeft,
    decimal PerPeriod,
    bool Reached,
    bool Overdue);

/// One envelope, this month. Same four numbers the savings pot always had — envelopes are
/// that idea applied to every bucket that is not spending money.
/// <param name="StillToReserve">Goal not yet moved by hand. This is what hides from
/// safe-to-spend; a deposit already made hides through <see cref="DepositedThisMonth"/>,
/// so the same money is never held back twice.</param>
/// <param name="IsFromScheme">A bucket in the active scheme carries this name, so the scheme
/// owns the envelope: its goal, and — because bucket and envelope are matched by name — its
/// name too. Sent to the screen so it can say why renaming is not on offer here.</param>
/// <param name="Target">What the jar is being filled up to, if the user set that — read only,
/// it reserves nothing (<see cref="FinanceApp.Domain.Savings.EnvelopeTargets"/>).</param>
public record EnvelopeStatus(
    int Id,
    string Name,
    BucketKind Kind,
    bool IsDefault,
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve,
    bool IsFromScheme = false,
    EnvelopeTargetStatus? Target = null)
{
    /// What this envelope takes out of "скільки можна витратити" this month.
    public decimal HeldBack => DepositedThisMonth + StillToReserve;
}

public interface IEnvelopeService
{
    /// Every envelope with its balance and this month's goal, in scheme order.
    /// <param name="month">The budget AND the window it covers, deliberately one argument.
    /// They used to be two — an amount plus an optional "counted on" date — and the savings
    /// screen passed the amount while forgetting the date. The goals then stood down on the
    /// home screen and not on the savings one, so every page load undid what the previous
    /// one wrote: the app's own deposit was deleted and re-created under a new id, the
    /// balance flipped between two numbers depending on which screen was open last, and
    /// editing a movement the other screen had just deleted answered «Операцію не знайдено».
    /// One argument cannot be half-passed.</param>
    Task<IReadOnlyList<EnvelopeStatus>> StatusAsync(
        MonthlyBudgetResult month, CancellationToken ct = default);

    /// One envelope, period by period: what moved and what the balance became. The screen
    /// used to show a flat list of movements, which answers "що я робив" but not the
    /// question actually being asked — «за місяць скільки пішло в заощадження і скільки
    /// там тепер». Periods, not calendar months, so it lines up with everything else.
    Task<IReadOnlyList<EnvelopePeriod>> HistoryAsync(
        int envelopeId, int count = 6, CancellationToken ct = default);

    /// A pot the user made up themselves — «Відпустка», «Ремонт» — rather than one a scheme
    /// bucket brought along. The word "банка" invites exactly this, and until now the only way
    /// to get one was to add a percentage bucket to the scheme.
    Task<Result<Envelope>> CreateAsync(string name, BucketKind kind, CancellationToken ct = default);

    /// Renames an envelope and/or changes what kind of pot it is. Only ever a hand-made one:
    /// the default envelope is found by name, and a scheme's envelope IS its bucket's name.
    Task<Result<Envelope>> UpdateAsync(int id, string name, BucketKind kind, CancellationToken ct = default);

    /// Puts an empty envelope away: it leaves the list and stops being somewhere to put money,
    /// while its movements stay readable. Refused while there is still money in it — a pot that
    /// vanished with a balance inside would take that money out of «Відкладено всього», and out
    /// of the only figure this app asks the user to trust.
    Task<Result<Envelope>> ArchiveAsync(int id, CancellationToken ct = default);

    /// «Відпустка 6 000 до червня» → «950 за період». A null amount takes the target off again.
    /// Nothing here reserves money: the pace is what the user needs to decide with, and holding
    /// it back automatically would compete with the allocation scheme for the same money.
    /// <param name="currency">What the amount was typed in; converted to base at today's rate,
    /// the same way a movement into a jar is. Null means base currency.</param>
    Task<Result<Envelope>> SetTargetAsync(
        int id, decimal? amount, string? currency, DateOnly? date, CancellationToken ct = default);

    /// What is in one jar right now. Public because taking money out of a jar happens in more
    /// than one place — a withdrawal, an expense, a debt repayment — and every one of them has
    /// to be checked against the same figure, or a jar can be emptied twice.
    Task<decimal> BalanceAsync(int envelopeId, CancellationToken ct = default);
}

/// <param name="Moved">Net movement over the period: deposits minus withdrawals minus
/// anything paid straight out of the envelope. Negative means the pot shrank.</param>
/// <param name="BalanceAfter">What was in the envelope when the period ended — or right
/// now, for the period still running.</param>
public record EnvelopePeriod(DateOnly Start, DateOnly End, decimal Moved, decimal BalanceAfter);

public sealed class EnvelopeService(
    IAppDbContext db, IAllocationService allocations, IBudgetPeriods periods,
    IFxConverter fx, IDebtLedger debts, ILogger<EnvelopeService> log) : IEnvelopeService
{
    public async Task<IReadOnlyList<EnvelopeStatus>> StatusAsync(
        MonthlyBudgetResult month, CancellationToken ct = default)
    {
        // The day an opening balance was taken, when the period started mid-way. That figure
        // is what is left to LIVE on: whatever was meant to be put aside either already was —
        // and is therefore already outside the counted money — or is out of reach this period.
        // So goals stand down, and only deposits made SINCE the count are held back.
        // Reserving percentages of the remainder again would drop the daily norm to almost
        // nothing, which is the exact problem the opening balance exists to fix.
        var countedOn = month.FromOpeningBalance ? month.WindowStart : (DateOnly?)null;

        var scheme = await allocations.GetActiveAsync(ct);
        var goals = countedOn is null
            ? await GoalsAsync(scheme, month.Budget ?? 0m, ct)
            : [];
        var envelopes = await SyncAsync(scheme, ct);
        var period = await periods.CurrentAsync(ct);
        await FillAsync(scheme, envelopes, goals, period, countedOn, ct);

        var balances = await db.SavingsEntries
            .GroupBy(x => x.EnvelopeId)
            .Select(g => new
            {
                EnvelopeId = g.Key,
                Balance = g.Sum(x => x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase),
            })
            .ToDictionaryAsync(x => x.EnvelopeId, x => x.Balance, ct);

        var (first, last) = period;
        var from = countedOn ?? first;
        // AlreadySetAside is left out: that money was put away before this period and was
        // never part of its budget, so charging the daily norm for it would take the same
        // złoty twice. It still counts towards the balance above — the jar really does hold it.
        var thisMonth = await db.SavingsEntries
            .Where(x => x.Date >= from && x.Date <= last && !x.AlreadySetAside)
            .GroupBy(x => x.EnvelopeId)
            .Select(g => new
            {
                EnvelopeId = g.Key,
                Net = g.Sum(x => x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase),
            })
            .ToDictionaryAsync(x => x.EnvelopeId, x => x.Net, ct);

        // Money spent straight out of an envelope leaves it the same way a withdrawal does.
        // Without this the pot would keep showing money that has already been paid to a shop
        // — and the expense, which the summary excludes precisely because the envelope
        // already holds it back, would vanish from the app's arithmetic entirely.
        var spentFrom = await db.Transactions
            .Where(t => t.EnvelopeId != null && t.Kind == TransactionKind.Expense)
            .Select(t => new { EnvelopeId = t.EnvelopeId!.Value, t.Date, t.AmountBase })
            .ToListAsync(ct);

        // A debt repaid out of a jar leaves it exactly like an expense does. It is not a
        // transaction — debts are kept out of categories and statistics on purpose — so it
        // has to be added here by name, or the jar would go on showing money already handed
        // back to whoever was owed it.
        spentFrom = spentFrom
            .Concat((await debts.FromEnvelopesAsync(ct))
                .Select(p => new { p.EnvelopeId, p.Date, p.AmountBase }))
            .ToList();

        foreach (var group in spentFrom.GroupBy(t => t.EnvelopeId))
        {
            balances[group.Key] = balances.GetValueOrDefault(group.Key) - group.Sum(t => t.AmountBase);
            thisMonth[group.Key] = thisMonth.GetValueOrDefault(group.Key)
                - group.Where(t => t.Date >= from && t.Date <= last).Sum(t => t.AmountBase);
        }

        // Bucket order first, then whatever is left over from an older scheme: an envelope
        // whose bucket is gone keeps its balance but no longer reserves anything.
        var order = scheme.Buckets
            .OrderBy(b => b.SortOrder)
            .Select((b, i) => (b.Name, Index: i))
            .ToDictionary(x => x.Name, x => x.Index);

        // Whose name belongs to the scheme. Read from the buckets themselves and not from
        // goals: a stood-down plan leaves goals empty, and the scheme still owns the name.
        var fromScheme = scheme.Buckets
            .Where(b => b.Kind != BucketKind.Spending)
            .Select(b => b.Name)
            .ToHashSet();

        var ordered = envelopes
            .OrderBy(e => order.TryGetValue(e.Name, out var i) ? i : int.MaxValue)
            .ThenBy(e => e.Id)
            .ToList();

        var result = new List<EnvelopeStatus>(ordered.Count);
        foreach (var e in ordered)
        {
            var goal = goals.GetValueOrDefault(e.Name);
            var deposited = thisMonth.GetValueOrDefault(e.Id);
            var status = SavingsCalculator.Status(goal, balances.GetValueOrDefault(e.Id), deposited);
            result.Add(new EnvelopeStatus(
                e.Id, e.Name, e.Kind, e.IsDefault,
                status.Balance, status.MonthGoal, status.DepositedThisMonth, status.StillToReserve,
                fromScheme.Contains(e.Name),
                await TargetAsync(e, status.Balance, period, ct)));
        }

        return result;
    }

    public async Task<IReadOnlyList<EnvelopePeriod>> HistoryAsync(
        int envelopeId, int count = 6, CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 24);

        var entries = await db.SavingsEntries
            .Where(x => x.EnvelopeId == envelopeId)
            .Select(x => new { x.Date, Amount = x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase })
            .ToListAsync(ct);

        var spent = await db.Transactions
            .Where(t => t.EnvelopeId == envelopeId && t.Kind == TransactionKind.Expense)
            .Select(t => new { t.Date, Amount = -t.AmountBase })
            .ToListAsync(ct);

        var repaid = (await debts.FromEnvelopesAsync(ct))
            .Where(p => p.EnvelopeId == envelopeId)
            .Select(p => new { p.Date, Amount = -p.AmountBase });

        var movements = entries.Concat(spent).Concat(repaid).ToList();

        // Walked forwards from the oldest period so the running balance is a sum, not a
        // series of subtractions from today — the same money counted the other way round
        // is where rounding drift comes from.
        var current = await periods.CurrentAsync(ct);
        var window = new List<BudgetPeriod> { current };
        for (var i = 1; i < count; i++)
            window.Add(await periods.ForAsync(window[^1].Start.AddDays(-1), ct));
        window.Reverse();

        var running = movements.Where(m => m.Date < window[0].Start).Sum(m => m.Amount);
        var result = new List<EnvelopePeriod>(window.Count);

        foreach (var p in window)
        {
            var moved = movements.Where(m => m.Date >= p.Start && m.Date <= p.End).Sum(m => m.Amount);
            running += moved;
            result.Add(new EnvelopePeriod(p.Start, p.End, moved, running));
        }

        // Newest first: the period you are living in is the one you came here to look at.
        result.Reverse();
        return result;
    }

    public async Task<Result<Envelope>> CreateAsync(
        string name, BucketKind kind, CancellationToken ct = default)
    {
        name = name.Trim();

        if (kind == BucketKind.Spending)
            return Error.Validation("Витрати — це не банка: банка тримає гроші, які ти не витрачаєш.");

        var same = await db.Envelopes.FirstOrDefaultAsync(e => e.Name == name, ct);
        if (same is not null)
        {
            // An archived envelope comes back instead of being duplicated: the balance and the
            // history under that name are the ones the user means, and the unique index on the
            // name would refuse a second row anyway.
            if (!same.IsArchived)
                return Error.Conflict($"Банка «{name}» вже є.");

            same.ArchivedAt = null;
            same.Kind = kind;
            await db.SaveChangesAsync(ct);
            log.LogInformation("Envelopes: «{Envelope}» un-archived instead of created anew", name);
            return Result<Envelope>.Ok(same);
        }

        var envelope = New(name, kind, isDefault: false);
        db.Envelopes.Add(envelope);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Envelopes: «{Envelope}» created by hand ({Kind})", name, kind);
        return Result<Envelope>.Ok(envelope);
    }

    public async Task<Result<Envelope>> UpdateAsync(
        int id, string name, BucketKind kind, CancellationToken ct = default)
    {
        name = name.Trim();

        if (kind == BucketKind.Spending)
            return Error.Validation("Витрати — це не банка: банка тримає гроші, які ти не витрачаєш.");

        var envelope = await db.Envelopes.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envelope is null) return Error.NotFound($"Банку {id} не знайдено.");

        if (envelope.Name != name)
        {
            var blocked = await RenameBlockedAsync(envelope, ct);
            if (blocked is not null) return blocked;

            if (await db.Envelopes.AnyAsync(e => e.Name == name && e.Id != id, ct))
                return Error.Conflict($"Банка «{name}» вже є.");

            log.LogInformation("Envelopes: «{Was}» renamed to «{Now}»", envelope.Name, name);
            envelope.Name = name;
        }

        envelope.Kind = kind;
        await db.SaveChangesAsync(ct);
        return Result<Envelope>.Ok(envelope);
    }

    public async Task<Result<Envelope>> ArchiveAsync(int id, CancellationToken ct = default)
    {
        var envelope = await db.Envelopes.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envelope is null) return Error.NotFound($"Банку {id} не знайдено.");
        if (envelope.IsArchived) return Result<Envelope>.Ok(envelope);

        if (envelope.IsDefault)
            return Error.Validation(
                "Це банка за замовчуванням — гроші, які нікуди більше не вказали, йдуть сюди. " +
                "Її не прибрати.");

        // A scheme envelope would be created again by the next screen load, and money would be
        // poured into it by the scheme — the removal would look like it undid itself.
        var scheme = await allocations.GetActiveAsync(ct);
        if (scheme.Buckets.Any(b => b.Kind != BucketKind.Spending && b.Name == envelope.Name))
            return Error.Validation(
                $"Цю банку наповнює схема «{scheme.Name}». Прибери кошик «{envelope.Name}» зі схеми — " +
                "тоді банку можна буде прибрати.");

        var balance = await BalanceAsync(id, ct);
        if (balance != 0m)
            return Error.Validation(
                $"У банці ще {balance:0.00}. Зніми ці гроші або перекинь в іншу банку — " +
                "порожню банку прибрати можна будь-коли.");

        envelope.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Envelopes: «{Envelope}» archived", envelope.Name);
        return Result<Envelope>.Ok(envelope);
    }

    public async Task<Result<Envelope>> SetTargetAsync(
        int id, decimal? amount, string? currency, DateOnly? date, CancellationToken ct = default)
    {
        var envelope = await db.Envelopes.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (envelope is null) return Error.NotFound($"Банку {id} не знайдено.");

        if (amount is null)
        {
            // The date goes with it: a date on its own says nothing, and left behind it would
            // come back the next time an amount was typed.
            envelope.TargetAmount = null;
            envelope.TargetDate = null;
            await db.SaveChangesAsync(ct);
            log.LogInformation("Envelopes: «{Envelope}» no longer has a target", envelope.Name);
            return Result<Envelope>.Ok(envelope);
        }

        if (amount <= 0) return Error.Validation("Ціль має бути більшою за нуль.");

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (date is { } wanted && wanted < today)
            return Error.Validation("Дата цілі вже минула — постав ту, до якої ще збираєш.");

        var conv = await fx.ConvertToBaseAsync(
            amount.Value, string.IsNullOrWhiteSpace(currency) ? Money.BaseCurrency : currency.ToUpperInvariant(),
            today, ct);
        if (!conv.IsSuccess) return conv.Error;

        envelope.TargetAmount = conv.Value!.AmountBase;
        envelope.TargetDate = date;
        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Envelopes: «{Envelope}» aims at {Amount} by {Date}",
            envelope.Name, envelope.TargetAmount, date?.ToString("yyyy-MM-dd") ?? "no date");
        return Result<Envelope>.Ok(envelope);
    }

    /// Why this envelope's name is not the user's to change. Both reasons are the same one:
    /// something else in the app looks the envelope up BY NAME, so a rename would quietly
    /// hand the balance to a pot nobody is feeding.
    private async Task<Error?> RenameBlockedAsync(Envelope envelope, CancellationToken ct)
    {
        if (envelope.IsDefault)
            return Error.Validation(
                $"«{Envelope.DefaultName}» — банка за замовчуванням, її назву застосунок шукає сам. " +
                "Зроби нову банку під ціль, якщо потрібна інша назва.");

        var scheme = await allocations.GetActiveAsync(ct);
        if (scheme.Buckets.Any(b => b.Kind != BucketKind.Spending && b.Name == envelope.Name))
            return Error.Validation(
                $"Назву цієї банки задає схема «{scheme.Name}». Перейменуй кошик «{envelope.Name}» у схемі — " +
                "банка перейменується разом із ним.");

        return null;
    }

    /// The target and what it asks of this period. Counted in budget periods, because that is
    /// the rhythm money arrives in — «на місяць» would be a figure the user never sees confirmed
    /// by a payday.
    private async Task<EnvelopeTargetStatus?> TargetAsync(
        Envelope envelope, decimal balance, BudgetPeriod current, CancellationToken ct)
    {
        if (envelope.TargetAmount is not { } target) return null;

        var pace = EnvelopeTargets.Pace(
            target, balance, await periods.CountUntilAsync(envelope.TargetDate, current, ct));

        return new EnvelopeTargetStatus(
            target, envelope.TargetDate,
            pace.Remaining, pace.PeriodsLeft, pace.PerPeriod, pace.Reached, pace.Overdue);
    }

    /// What is in one envelope right now: movements in and out, less anything paid straight
    /// out of it. The same arithmetic StatusAsync does for the list, for a single pot.
    public async Task<decimal> BalanceAsync(int envelopeId, CancellationToken ct = default)
    {
        var moved = await db.SavingsEntries
            .Where(x => x.EnvelopeId == envelopeId)
            .SumAsync(x => x.Kind == SavingsEntryKind.Deposit ? x.AmountBase : -x.AmountBase, ct);

        var spent = await db.Transactions
            .Where(t => t.EnvelopeId == envelopeId && t.Kind == TransactionKind.Expense)
            .SumAsync(t => (decimal?)t.AmountBase, ct) ?? 0m;

        var repaid = (await debts.FromEnvelopesAsync(ct))
            .Where(p => p.EnvelopeId == envelopeId)
            .Sum(p => p.AmountBase);

        return moved - spent - repaid;
    }

    /// Carries out the scheme instead of asking the user to. Choosing «20% у заощадження»
    /// used to mean only that 20% was subtracted from the daily norm — the pot itself stayed
    /// empty until money was moved into it by hand, every single month. That is exactly the
    /// kind of standing chore this app exists to remove, so the app moves it.
    ///
    /// One entry per envelope per period, kept in step with the goal rather than topped up:
    /// a second invoice raises the budget, and a trail of correcting deposits would make the
    /// envelope's history unreadable.
    ///
    /// A deposit made BY HAND is now extra, on top of the plan — it used to be the only way
    /// to meet the goal, so it counted towards it. Now the app meets the goal itself, and
    /// someone who still moves money in means "more than planned". Withdrawals are left
    /// alone: taking money out is a decision, and refilling it on the next page load would
    /// silently overrule it.
    ///
    /// No goals at all means the plan has stood down — the user started mid-period by
    /// counting what they have (see the countedOn parameter on StatusAsync). Then what the
    /// app had already set aside on paper is withdrawn too: the counted figure is money the
    /// user says is theirs to live on, and holding some of it back again would take the
    /// daily norm apart from underneath.
    private async Task FillAsync(
        AllocationScheme scheme, List<Envelope> envelopes, Dictionary<string, decimal> goals,
        BudgetPeriod period, DateOnly? countedOn, CancellationToken ct)
    {
        var entries = await db.SavingsEntries
            .Where(x => x.Date >= period.Start && x.Date <= period.End)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var changed = false;

        // Logged because this method writes to the database while merely reading a screen,
        // and every number the user then argues with comes out of these three decisions.
        // Standing down is Debug: it is true for every read of the period, not an event.
        if (countedOn is { } counted)
            log.LogDebug(
                "Envelopes: plan stood down for {Start:yyyy-MM-dd}..{End:yyyy-MM-dd} — balance counted on {Counted:yyyy-MM-dd}",
                period.Start, period.End, counted);

        foreach (var envelope in envelopes)
        {
            var amount = goals.GetValueOrDefault(envelope.Name);

            // There must be exactly one of these per envelope per period. There can be more,
            // because reading a screen writes: the home page fires several queries at once and
            // every one of them resolves the envelopes, so two requests can both find nothing
            // here and both insert. Whichever landed second was then never looked at again —
            // the lookup below only ever took the first — so it sat in the jar for good,
            // inflating the balance AND holding its amount back from the daily norm a second
            // time. That is a plan the user never made, taking money they can see in the bank.
            //
            // Cleaned up rather than merely prevented: locking would only stop the next one,
            // and every database that already raced would stay wrong forever.
            var mine = entries
                .Where(x => x.EnvelopeId == envelope.Id && x.IsAuto)
                .OrderBy(x => x.Id)
                .ToList();

            foreach (var duplicate in mine.Skip(1))
            {
                db.SavingsEntries.Remove(duplicate);
                changed = true;
                log.LogWarning(
                    "Envelopes: «{Envelope}» had {Count} scheme deposits for the period — removed a duplicate of {Amount}",
                    envelope.Name, mine.Count, duplicate.AmountBase);
            }

            var auto = mine.FirstOrDefault();

            if (auto is null)
            {
                if (amount <= 0) continue;

                db.SavingsEntries.Add(new SavingsEntry
                {
                    EnvelopeId = envelope.Id,
                    Date = today,
                    Kind = SavingsEntryKind.Deposit,
                    AmountOriginal = amount,
                    CurrencyOriginal = Money.BaseCurrency,
                    AmountBase = amount,
                    FxRate = 1m,
                    FxDate = today,
                    IsAuto = true,
                    Note = $"За схемою «{scheme.Name}»",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                changed = true;
                log.LogInformation(
                    "Envelopes: «{Envelope}» filled with {Amount} by scheme «{Scheme}» for {Start:yyyy-MM-dd}",
                    envelope.Name, amount, scheme.Name, period.Start);
            }
            else if (amount <= 0)
            {
                // Removed, not zeroed: a 0 zł deposit in the envelope's history is a line
                // that says nothing and still has to be read.
                db.SavingsEntries.Remove(auto);
                changed = true;
                log.LogInformation(
                    "Envelopes: «{Envelope}» no longer has a goal — withdrew the {Amount} the scheme had set aside",
                    envelope.Name, auto.AmountBase);
            }
            else if (auto.AmountBase != amount)
            {
                log.LogInformation(
                    "Envelopes: «{Envelope}» re-poured {Was} → {Now} (scheme «{Scheme}», period from {Start:yyyy-MM-dd})",
                    envelope.Name, auto.AmountBase, amount, scheme.Name, period.Start);
                auto.AmountOriginal = amount;
                auto.AmountBase = amount;
                changed = true;
            }
        }

        if (changed) await db.SaveChangesAsync(ct);
    }

    /// This month's goal per envelope name. A scheme bucket owns the goal when there is one;
    /// otherwise the savings plan feeds the default envelope — two mechanisms reserving for
    /// the same pot at once would hold the money twice.
    private async Task<Dictionary<string, decimal>> GoalsAsync(
        AllocationScheme scheme, decimal monthlyBudget, CancellationToken ct)
    {
        var breakdown = await allocations.BreakdownAsync(monthlyBudget, ct);

        var goals = breakdown.Shares
            .Where(s => s.Kind != BucketKind.Spending)
            .ToDictionary(s => s.Name, s => s.Amount);

        if (!goals.ContainsKey(Envelope.DefaultName))
        {
            var plan = await db.SavingsPlans.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
            var fromPlan = SavingsCalculator.MonthGoal(plan, monthlyBudget);
            if (fromPlan > 0) goals[Envelope.DefaultName] = fromPlan;
        }

        return goals;
    }

    /// Makes sure every non-spending bucket has a pot to actually put money in, and that the
    /// default one always exists. Reading creates rows, like recurring materialization does:
    /// the alternative is a scheme that promises a pension bucket the user cannot deposit to.
    ///
    /// Archived envelopes are read too, and matched by name: an archived pot whose name a
    /// bucket carries comes back rather than being duplicated — the money under that name is
    /// what the bucket means, and the unique index would refuse the second row anyway.
    private async Task<List<Envelope>> SyncAsync(AllocationScheme scheme, CancellationToken ct)
    {
        var existing = await db.Envelopes.ToListAsync(ct);
        var byName = existing.ToDictionary(e => e.Name);
        var added = false;

        if (!existing.Any(e => e.IsDefault && !e.IsArchived))
        {
            // Adopt a same-named envelope rather than adding a second one — the unique index
            // on the name would reject it anyway, and the balance belongs to that name.
            if (byName.TryGetValue(Envelope.DefaultName, out var same))
            {
                same.IsDefault = true;
                same.ArchivedAt = null;
            }
            else
            {
                var def = New(Envelope.DefaultName, BucketKind.Savings, isDefault: true);
                db.Envelopes.Add(def);
                existing.Add(def);
                byName[def.Name] = def;
            }
            added = true;
        }

        foreach (var bucket in scheme.Buckets.Where(b => b.Kind != BucketKind.Spending))
        {
            if (byName.TryGetValue(bucket.Name, out var known))
            {
                if (!known.IsArchived) continue;

                known.ArchivedAt = null;
                added = true;
                log.LogInformation(
                    "Envelopes: «{Envelope}» came back — the scheme has a bucket by that name again",
                    known.Name);
                continue;
            }

            var e = New(bucket.Name, bucket.Kind, isDefault: false);
            db.Envelopes.Add(e);
            existing.Add(e);
            byName[e.Name] = e;
            added = true;
        }

        if (added) await db.SaveChangesAsync(ct);

        // Put-away pots are not filled and not shown; their movements stay reachable by id.
        return existing.Where(e => !e.IsArchived).ToList();
    }

    private static Envelope New(string name, BucketKind kind, bool isDefault) => new()
    {
        Name = name, Kind = kind, IsDefault = isDefault, CreatedAt = DateTimeOffset.UtcNow,
    };
}
