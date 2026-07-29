using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Common;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Budgets;

public interface IOpeningBalanceService
{
    Task<OpeningBalanceResponse> GetAsync(CancellationToken ct = default);
    Task<Result<OpeningBalanceResponse>> SetAsync(SetOpeningBalanceRequest req, CancellationToken ct = default);
    Task<OpeningBalanceResponse> ClearAsync(CancellationToken ct = default);
}

/// One row, overwritten. A second count replaces the first — keeping a history of guesses
/// about the same month would only raise the question of which one is in force.
public sealed class OpeningBalanceService(
    IAppDbContext db, IFxConverter fx, IMoneyViewFactory moneyViews,
    IBudgetPeriods periods) : IOpeningBalanceService
{
    public async Task<OpeningBalanceResponse> GetAsync(CancellationToken ct = default) =>
        await ShowAsync(await RowAsync(ct), ct);

    public async Task<Result<OpeningBalanceResponse>> SetAsync(
        SetOpeningBalanceRequest req, CancellationToken ct = default)
    {
        if (req.Amount < 0)
            return Error.Validation("Залишок не може бути від'ємним.");

        var today = DateOnly.FromDateTime(DateTime.Now);
        var date = req.Date ?? today;
        if (date > today)
            return Error.Validation("Не можна порахувати залишок наперед.");

        var view = await moneyViews.CurrentAsync(ct);
        var currency = string.IsNullOrWhiteSpace(req.Currency)
            ? view.Currency
            : req.Currency.ToUpperInvariant();

        var conv = await fx.ConvertToBaseAsync(req.Amount, currency, date, ct);
        if (!conv.IsSuccess) return conv.Error;

        var row = await RowAsync(ct);
        if (row is null)
        {
            row = new OpeningBalance();
            db.OpeningBalances.Add(row);
        }

        row.Date = date;
        row.AmountOriginal = req.Amount;
        row.CurrencyOriginal = currency;
        row.AmountBase = conv.Value!.AmountBase;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result<OpeningBalanceResponse>.Ok(await ShowAsync(row, ct));
    }

    /// For "I want the ordinary month back" — and for undoing a wrong number without
    /// waiting a month for it to expire.
    public async Task<OpeningBalanceResponse> ClearAsync(CancellationToken ct = default)
    {
        var row = await RowAsync(ct);
        if (row is not null)
        {
            db.OpeningBalances.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return await ShowAsync(null, ct);
    }

    private Task<OpeningBalance?> RowAsync(CancellationToken ct) =>
        db.OpeningBalances.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

    private async Task<OpeningBalanceResponse> ShowAsync(OpeningBalance? row, CancellationToken ct)
    {
        var view = await moneyViews.CurrentAsync(ct);
        if (row is null) return new OpeningBalanceResponse(false, null, view.Currency, null, false);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var (first, _) = await periods.CurrentAsync(ct);

        return new OpeningBalanceResponse(
            true,
            await view.FromBaseTodayAsync(row.AmountBase, ct),
            view.Currency,
            row.Date,
            // Says out loud whether an earlier count is still steering the daily norm.
            row.Date >= first && row.Date <= today);
    }
}
