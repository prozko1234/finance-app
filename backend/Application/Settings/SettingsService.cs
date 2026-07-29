using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Budgeting;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Settings;

public interface ISettingsService
{
    Task<AppSettingsResponse> GetAsync(CancellationToken ct = default);
    Task<Result<AppSettingsResponse>> SetDisplayCurrencyAsync(string currency, CancellationToken ct = default);

    /// The day money arrives. Everything that says "this period" follows it
    /// (<see cref="Domain.Budgeting.BudgetPeriods"/>).
    Task<Result<AppSettingsResponse>> SetPeriodStartDayAsync(int day, CancellationToken ct = default);
}

public sealed class SettingsService(IAppDbContext db, IFxConverter fx) : ISettingsService
{
    public async Task<AppSettingsResponse> GetAsync(CancellationToken ct = default)
    {
        var s = await db.AppSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return Map(s?.DisplayCurrency ?? Money.BaseCurrency, s?.PeriodStartDay ?? BudgetPeriods.FirstOfMonth);
    }

    public async Task<Result<AppSettingsResponse>> SetPeriodStartDayAsync(
        int day, CancellationToken ct = default)
    {
        // 28 is the last day that exists in every month. Beyond it a period would start on
        // a day February does not have, and "the 30th" would silently mean four different
        // dates a year — a настройка that cannot be trusted is worse than one that is absent.
        if (day is < 1 or > 28)
            return Error.Validation("День має бути від 1 до 28 — інші числа є не в кожному місяці.");

        var s = await UpsertAsync(ct);
        s.PeriodStartDay = day;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Result<AppSettingsResponse>.Ok(Map(s.DisplayCurrency, s.PeriodStartDay));
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

        var s = await UpsertAsync(ct);
        s.DisplayCurrency = currency;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Result<AppSettingsResponse>.Ok(Map(s.DisplayCurrency, s.PeriodStartDay));
    }

    /// MVP: a single settings row — the first one, created on demand, like Budget.
    private async Task<AppSettings> UpsertAsync(CancellationToken ct)
    {
        var s = await db.AppSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (s is not null) return s;

        s = new AppSettings { UpdatedAt = DateTimeOffset.UtcNow };
        db.AppSettings.Add(s);
        return s;
    }

    private static AppSettingsResponse Map(string displayCurrency, int periodStartDay)
    {
        var period = BudgetPeriods.For(DateOnly.FromDateTime(DateTime.Now), periodStartDay);

        return new AppSettingsResponse(
            displayCurrency,
            Money.BaseCurrency,
            // The tax engine is Polish and computes in PLN. When the user reads another
            // currency the app has to say so out loud rather than quietly present a
            // converted number.
            TaxesInBaseCurrency: displayCurrency != Money.BaseCurrency,
            periodStartDay,
            period.Start,
            period.End);
    }
}
