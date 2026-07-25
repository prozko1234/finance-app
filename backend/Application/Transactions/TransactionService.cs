using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Mapping;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Transactions;

public sealed class TransactionService(
    IAppDbContext db, IFxConverter fx, IRecurringMaterializer materializer) : ITransactionService
{
    public async Task<IReadOnlyList<TransactionResponse>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await materializer.MaterializeDueAsync(ct); // reflect any due recurring in the list

        var items = await db.Transactions
            .Include(t => t.Category)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        return items.Select(t => t.ToResponse()).ToList();
    }

    public async Task<Result<TransactionResponse>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var t = await db.Transactions.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null
            ? Error.NotFound($"Транзакцію {id} не знайдено.")
            : Result<TransactionResponse>.Ok(t.ToResponse());
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
        return Result<TransactionResponse>.Ok(tx.ToResponse());
    }

    /// Income entry: the user types what arrived (gross with VAT, or net), we split VAT out
    /// and store the revenue. Monthly taxes are applied later over the month's total revenue,
    /// never per invoice — see SummaryService.
    public async Task<Result<TransactionResponse>> CreateIncomeAsync(
        SaveIncomeRequest req, CancellationToken ct = default)
    {
        var date = req.Date ?? DateOnly.FromDateTime(DateTime.Now);

        var conv = await fx.ConvertToBaseAsync(req.Amount, req.Currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;
        var enteredBase = conv.Value!.AmountBase;

        var profile = await db.TaxProfiles.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        var vatRate = profile is { VatPayer: true } ? profile.VatRate : 0m;

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

        var category = await db.Categories.OrderBy(c => c.Id).FirstAsync(ct);
        var tx = new Transaction
        {
            Kind = TransactionKind.Income,
            AmountOriginal = req.Amount,
            CurrencyOriginal = req.Currency.ToUpperInvariant(),
            AmountBase = revenue,          // revenue (VAT excluded) is what taxes build on
            GrossWithVat = grossWithVat,
            VatAmount = Math.Round(grossWithVat - revenue, 2, MidpointRounding.AwayFromZero),
            FxRate = conv.Value.Rate,
            FxDate = conv.Value.RateDate,
            CategoryId = category.Id,
            Priority = Priority.Must,
            Frequency = Frequency.OneOff,
            Source = TxSource.Manual,
            Date = date,
            Note = req.Note,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);
        await LoadCategoryAsync(tx, ct);
        return Result<TransactionResponse>.Ok(tx.ToResponse());
    }

    public async Task<Result<TransactionResponse>> UpdateAsync(int id, SaveTransactionRequest req, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tx is null) return Error.NotFound($"Транзакцію {id} не знайдено.");

        var applied = await ApplyAsync(tx, req, fallbackDate: tx.Date, ct);
        if (!applied.IsSuccess) return applied.Error;

        await db.SaveChangesAsync(ct);
        await LoadCategoryAsync(tx, ct);
        return Result<TransactionResponse>.Ok(tx.ToResponse());
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tx is null) return Error.NotFound($"Транзакцію {id} не знайдено.");

        db.Transactions.Remove(tx);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    /// Shared for create/update: validate category, convert currency, write fields.
    private async Task<Result<bool>> ApplyAsync(Transaction tx, SaveTransactionRequest req, DateOnly? fallbackDate, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId, ct))
            return Error.Validation($"Категорію {req.CategoryId} не знайдено.");

        var date = req.Date ?? fallbackDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var conv = await fx.ConvertToBaseAsync(req.Amount, req.Currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        tx.AmountOriginal = req.Amount;
        tx.CurrencyOriginal = req.Currency.ToUpperInvariant();
        tx.AmountBase = conv.Value!.AmountBase;
        tx.FxRate = conv.Value.Rate;
        tx.FxDate = conv.Value.RateDate;
        tx.CategoryId = req.CategoryId;
        tx.Priority = req.Priority;
        tx.Frequency = req.Frequency;
        tx.Date = date;
        tx.MerchantRaw = req.Merchant;
        tx.Note = req.Note;
        return Result<bool>.Ok(true);
    }

    private async Task LoadCategoryAsync(Transaction tx, CancellationToken ct) =>
        tx.Category = await db.Categories.FirstOrDefaultAsync(c => c.Id == tx.CategoryId, ct);
}
