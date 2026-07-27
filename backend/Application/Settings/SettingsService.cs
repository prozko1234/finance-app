using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Settings;

public interface ISettingsService
{
    Task<AppSettingsResponse> GetAsync(CancellationToken ct = default);
    Task<Result<AppSettingsResponse>> SetDisplayCurrencyAsync(string currency, CancellationToken ct = default);
}

public sealed class SettingsService(IAppDbContext db, IFxConverter fx) : ISettingsService
{
    public async Task<AppSettingsResponse> GetAsync(CancellationToken ct = default)
    {
        var s = await db.AppSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return Map(s?.DisplayCurrency ?? Money.BaseCurrency);
    }

    public async Task<Result<AppSettingsResponse>> SetDisplayCurrencyAsync(
        string currency, CancellationToken ct = default)
    {
        currency = currency.ToUpperInvariant();

        // A currency nobody can quote is not a choice, it is a broken screen later. Prove a
        // rate exists before storing it, and fail here where the user can still pick another.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var probe = await fx.ConvertFromBaseAsync(1m, currency, today, ct);
        if (!probe.IsSuccess)
            return Error.Unsupported($"Валюта {currency} недоступна: немає курсу. Обери іншу.");

        // MVP: a single settings row — upsert the first, like Budget.
        var s = await db.AppSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new AppSettings { DisplayCurrency = currency, UpdatedAt = DateTimeOffset.UtcNow };
            db.AppSettings.Add(s);
        }
        else
        {
            s.DisplayCurrency = currency;
            s.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        return Result<AppSettingsResponse>.Ok(Map(s.DisplayCurrency));
    }

    private static AppSettingsResponse Map(string displayCurrency) => new(
        displayCurrency,
        Money.BaseCurrency,
        // The tax engine is Polish and computes in PLN. When the user reads another currency
        // the app has to say so out loud rather than quietly present a converted number.
        TaxesInBaseCurrency: displayCurrency != Money.BaseCurrency);
}
