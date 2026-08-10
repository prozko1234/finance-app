using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Auth;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Mapping;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Transactions;

public sealed class TransactionService(
    IAppDbContext db, IFxConverter fx, IRecurringMaterializer materializer,
    IMoneyViewFactory moneyViews, IUserProvisioning provisioning) : ITransactionService
{
    /// Every transaction leaving this service carries the amount as the user reads it,
    /// converted at the transaction's OWN date: a row is a record of what happened, and
    /// re-reading it at today's rate would quietly resize the past.
    private async Task<TransactionResponse> ShowAsync(
        Transaction t, MoneyView view, CancellationToken ct) =>
        t.ToResponse() with
        {
            AmountDisplay = await view.FromBaseAsync(t.AmountBase, t.Date, ct),
            DisplayCurrency = view.Currency,
        };

    private async Task<TransactionResponse> ShowAsync(Transaction t, CancellationToken ct) =>
        await ShowAsync(t, await moneyViews.CurrentAsync(ct), ct);

    public async Task<IReadOnlyList<TransactionResponse>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await materializer.MaterializeDueAsync(ct); // reflect any due recurring in the list

        var items = await db.Transactions
            .Include(t => t.Category)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        // One view for the whole list: it caches a rate per distinct date, so a month of
        // transactions costs at most one lookup per day, not one per row.
        var view = await moneyViews.CurrentAsync(ct);
        var rows = new List<TransactionResponse>(items.Count);
        foreach (var t in items) rows.Add(await ShowAsync(t, view, ct));
        return rows;
    }

    public async Task<Result<TransactionResponse>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var t = await db.Transactions.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null
            ? Error.NotFound($"Транзакцію {id} не знайдено.")
            : Result<TransactionResponse>.Ok(await ShowAsync(t, ct));
    }

    public async Task<Result<TransactionResponse>> CreateAsync(SaveTransactionRequest req, CancellationToken ct = default)
    {
        var tx = new Transaction { CurrencyOriginal = req.Currency.ToUpperInvariant(), Source = TxSource.Manual };
        var applied = await ApplyAsync(tx, req, fallbackDate: null, ct);
        if (!applied.IsSuccess) return applied.Error;

        tx.CreatedAt = DateTimeOffset.UtcNow;
        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);
        await LoadCategoryAsync(tx, ct);
        return Result<TransactionResponse>.Ok(await ShowAsync(tx, ct));
    }

    /// Income entry: the user types what arrived (gross with VAT, or net), we split VAT out
    /// and store the revenue. Monthly taxes are applied later over the month's total revenue,
    /// never per invoice — see SummaryService.
    public async Task<Result<TransactionResponse>> CreateIncomeAsync(
        SaveIncomeRequest req, CancellationToken ct = default)
    {
        var categoryId = await IncomeCategoryAsync(req.CategoryId, ct);
        if (categoryId is null) return Error.Validation("Немає категорії надходжень.");

        var tx = new Transaction
        {
            Kind = TransactionKind.Income,
            CurrencyOriginal = req.Currency.ToUpperInvariant(),
            CategoryId = categoryId.Value,
            Frequency = Frequency.OneOff,
            Source = TxSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var applied = await ApplyIncomeAsync(tx, req, ct);
        if (!applied.IsSuccess) return applied.Error;

        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);
        await LoadCategoryAsync(tx, ct);
        return Result<TransactionResponse>.Ok(await ShowAsync(tx, ct));
    }

    /// Correcting an invoice instead of deleting it and typing it again. A separate path from
    /// the expense one on purpose: an income row keeps the revenue (przychód, VAT excluded) in
    /// <see cref="Transaction.AmountBase"/>, and the ordinary update — which knows nothing about
    /// VAT — would write the gross figure there and leave GrossWithVat and VatAmount describing
    /// the old amount. Nothing would look broken; the month's budget would simply be wrong by
    /// the VAT, which is the worst kind of bug this app can have.
    public async Task<Result<TransactionResponse>> UpdateIncomeAsync(
        int id, SaveIncomeRequest req, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tx is null) return Error.NotFound($"Транзакцію {id} не знайдено.");
        if (tx.Kind != TransactionKind.Income)
            return Error.Validation("Це витрата, а не дохід — редагується вона як витрата.");

        // "Від кого" is as correctable as the amount. An id from the expense list is refused
        // the same way it is on creation, and the row keeps the category it had.
        if (req.CategoryId is { } chosen && await db.Categories.AnyAsync(
                c => c.Id == chosen && c.Kind == CategoryKind.Income, ct))
            tx.CategoryId = chosen;

        var applied = await ApplyIncomeAsync(tx, req, ct);
        if (!applied.IsSuccess) return applied.Error;

        await db.SaveChangesAsync(ct);
        await LoadCategoryAsync(tx, ct);
        return Result<TransactionResponse>.Ok(await ShowAsync(tx, ct));
    }

    /// Where the money came from. A chosen category has to be an income one — filing a salary
    /// under "Продукти" is exactly what having two lists is meant to stop, and an id from the
    /// wrong list can only be a mistake, not a preference.
    ///
    /// Nothing chosen falls back to the account's income fallback, and to any income category
    /// after that: the form sends an id, but an invoice arriving from an older client — or
    /// from the reconcile screen, which has no category to offer — must still be writable.
    private async Task<int?> IncomeCategoryAsync(int? chosen, CancellationToken ct)
    {
        if (chosen is { } id)
        {
            var ok = await db.Categories.AnyAsync(
                c => c.Id == id && c.Kind == CategoryKind.Income, ct);
            if (ok) return id;
        }

        await provisioning.EnsureIncomeCategoriesAsync(ct);

        var fallback = await db.Categories
            .Where(c => c.Kind == CategoryKind.Income)
            .OrderByDescending(c => c.IsSystem).ThenBy(c => c.SortOrder)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        return fallback;
    }

    /// The VAT split, shared by writing an invoice and correcting one, so the two can never
    /// disagree about what «6 000 z VAT» means.
    private async Task<Result<bool>> ApplyIncomeAsync(
        Transaction tx, SaveIncomeRequest req, CancellationToken ct)
    {
        if (req.Amount <= 0) return Error.Validation("Сума має бути більшою за нуль.");

        var date = req.Date ?? (tx.Date == default ? DateOnly.FromDateTime(DateTime.Now) : tx.Date);
        var currency = req.Currency.ToUpperInvariant();

        var conv = await fx.ConvertToBaseAsync(req.Amount, currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;
        var enteredBase = conv.Value!.AmountBase;

        var vatRate = await VatRateForAsync(tx, ct);

        decimal grossWithVat, revenue;
        if (req.AmountIncludesVat)
        {
            grossWithVat = enteredBase;
            revenue = Math.Round(enteredBase / (1 + vatRate), 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            revenue = enteredBase;
            grossWithVat = Math.Round(enteredBase * (1 + vatRate), 2, MidpointRounding.AwayFromZero);
        }

        tx.AmountOriginal = req.Amount;
        tx.CurrencyOriginal = currency;
        tx.AmountBase = revenue;          // revenue (VAT excluded) is what taxes build on
        tx.GrossWithVat = grossWithVat;
        tx.VatAmount = Math.Round(grossWithVat - revenue, 2, MidpointRounding.AwayFromZero);
        tx.FxRate = conv.Value.Rate;
        tx.FxDate = conv.Value.RateDate;
        tx.Date = date;
        tx.Note = req.Note;

        return Result<bool>.Ok(true);
    }

    /// An invoice keeps the VAT treatment it was written under — the same rule the fx rate
    /// follows. Re-splitting an old invoice at today's rate because the user has since
    /// registered for VAT (or stopped) would rewrite a figure the tax office has already seen.
    /// A row that has no VAT figures of its own is new, so the profile decides.
    private async Task<decimal> VatRateForAsync(Transaction tx, CancellationToken ct)
    {
        if (tx.GrossWithVat is { } gross && tx.AmountBase > 0m)
            return Math.Round(gross / tx.AmountBase - 1m, 4, MidpointRounding.AwayFromZero);

        var profile = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return profile is { VatPayer: true } ? profile.VatRate : 0m;
    }

    public async Task<Result<TransactionResponse>> UpdateAsync(int id, SaveTransactionRequest req, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tx is null) return Error.NotFound($"Транзакцію {id} не знайдено.");
        // Not a detail of the UI: this path would put the gross amount into AmountBase, where
        // an income row keeps the revenue, and the month's budget would quietly gain the VAT.
        if (tx.Kind == TransactionKind.Income)
            return Error.Validation("Це дохід — його редагує форма доходу, бо там ще є VAT.");

        var applied = await ApplyAsync(tx, req, fallbackDate: tx.Date, ct);
        if (!applied.IsSuccess) return applied.Error;

        await db.SaveChangesAsync(ct);
        await LoadCategoryAsync(tx, ct);
        return Result<TransactionResponse>.Ok(await ShowAsync(tx, ct));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tx is null) return Error.NotFound($"Транзакцію {id} не знайдено.");

        // A charge written by a recurring rule needs its deletion remembered, not just done.
        // Materialization decides what is still owed by looking for a transaction on that
        // date — the very row being removed — so without this the next read writes it back
        // and deleting a subscription's expense looks like the app arguing with the user.
        if (tx.RecurringExpenseId is { } recurringId)
        {
            var alreadySkipped = await db.RecurringSkips
                .AnyAsync(s => s.RecurringExpenseId == recurringId && s.Date == tx.Date, ct);

            if (!alreadySkipped)
            {
                db.RecurringSkips.Add(new RecurringSkip
                {
                    RecurringExpenseId = recurringId,
                    Date = tx.Date,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        db.Transactions.Remove(tx);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    /// Shared for create/update: validate category, convert currency, write fields.
    private async Task<Result<bool>> ApplyAsync(Transaction tx, SaveTransactionRequest req, DateOnly? fallbackDate, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            return Error.Validation($"Категорію {req.CategoryId} не знайдено.");

        if (req.EnvelopeId is { } envelopeId
            && !await db.Envelopes.AnyAsync(e => e.Id == envelopeId && e.ArchivedAt == null, ct))
            return Error.Validation($"Банку {envelopeId} не знайдено.");

        var date = req.Date ?? fallbackDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var conv = await fx.ConvertToBaseAsync(req.Amount, req.Currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        tx.AmountOriginal = req.Amount;
        tx.CurrencyOriginal = req.Currency.ToUpperInvariant();
        tx.AmountBase = conv.Value!.AmountBase;
        tx.FxRate = conv.Value.Rate;
        tx.FxDate = conv.Value.RateDate;
        tx.CategoryId = req.CategoryId;
        // Deliberately not checked against the envelope's balance: unlike a withdrawal,
        // which is bookkeeping, a purchase has already happened. An envelope in the red is
        // the truth, and the screen shows it rather than refusing the entry.
        tx.EnvelopeId = req.EnvelopeId;
        tx.Frequency = req.Frequency;
        tx.Date = date;
        tx.MerchantRaw = req.Merchant;
        tx.Note = req.Note;
        return Result<bool>.Ok(true);
    }

    private async Task LoadCategoryAsync(Transaction tx, CancellationToken ct) =>
        tx.Category = await db.Categories.FirstOrDefaultAsync(c => c.Id == tx.CategoryId, ct);
}
