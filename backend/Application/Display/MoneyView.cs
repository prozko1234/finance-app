using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using FinanceApp.Domain.Fx;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Application.Display;

public interface IMoneyViewFactory
{
    /// Resolves how money is shown for this request. Cheap and safe to call per service.
    Task<MoneyView> CurrentAsync(CancellationToken ct = default);
}

/// How stored money is read out for one request. Storage is always
/// <see cref="Money.BaseCurrency"/>; this turns those amounts into whatever the user
/// chose to read, at the rate of the amount's own date.
///
/// Two rules earn their keep here. It never returns an unconverted number labelled as
/// another currency — a wrong number that looks right is worse than an honest PLN one, so
/// when no rate can be had at all the whole view falls back to base. And amounts are
/// converted at their own date, so what happened in July keeps its July size no matter
/// when it is read.
public sealed class MoneyView
{
    private readonly IFxConverter _fx;
    private readonly decimal _todayPerBase;
    private readonly DateOnly _today;
    // Base units per one display unit, by date. One entry per distinct date in a response —
    // a month has at most 31, and the converter caches them in the DB anyway.
    private readonly Dictionary<DateOnly, decimal> _rates = [];

    private MoneyView(string currency, IFxConverter fx, decimal todayPerBase, DateOnly today)
    {
        Currency = currency;
        _fx = fx;
        _todayPerBase = todayPerBase;
        _today = today;
    }

    /// The currency amounts are reported in — never a currency we failed to get a rate for.
    public string Currency { get; }

    public bool IsBase => Currency == Money.BaseCurrency;

    public static MoneyView Base(IFxConverter fx, DateOnly today) =>
        new(Money.BaseCurrency, fx, 1m, today);

    internal static MoneyView For(string currency, IFxConverter fx, decimal todayRate, DateOnly today) =>
        new(currency, fx, todayRate, today);

    /// A stored base amount as the user reads it, at the rate effective on <paramref name="date"/>.
    public async Task<decimal> FromBaseAsync(decimal baseAmount, DateOnly date, CancellationToken ct = default)
    {
        if (IsBase) return baseAmount;

        var rate = await RateOnAsync(date, ct);
        return Math.Round(baseAmount / rate, 2, MidpointRounding.AwayFromZero);
    }

    /// The other direction: a number the user typed in their own currency, turned into the
    /// base amount we store. Anything written back to the database goes through here, or the
    /// app would save hryvnia digits into a zloty column.
    public async Task<decimal> ToBaseTodayAsync(decimal displayAmount, CancellationToken ct = default)
    {
        if (IsBase) return displayAmount;

        var conv = await _fx.ConvertToBaseAsync(displayAmount, Currency, _today, ct);
        // The view only exists because today's rate was obtained, so this is all but
        // unreachable; refusing to guess is still better than storing a wrong anchor.
        return conv.IsSuccess ? conv.Value!.AmountBase : displayAmount * _todayPerBase;
    }

    /// For amounts that are about now rather than about a past record — the month budget,
    /// safe-to-spend, a savings goal. They have no date of their own, so they take today's.
    public Task<decimal> FromBaseTodayAsync(decimal baseAmount, CancellationToken ct = default) =>
        FromBaseAsync(baseAmount, _today, ct);

    private async Task<decimal> RateOnAsync(DateOnly date, CancellationToken ct)
    {
        if (_rates.TryGetValue(date, out var known)) return known;

        var r = await _fx.ConvertFromBaseAsync(1m, Currency, date, ct);
        // A single old date without a quote must not blank out the screen: today's rate is
        // proven to exist, and being a few groszy off on one row beats showing nothing.
        var rate = r.IsSuccess && r.Value!.Rate > 0 ? r.Value.Rate : _todayPerBase;
        _rates[date] = rate;
        return rate;
    }
}

public sealed class MoneyViewFactory(IAppDbContext db, IFxConverter fx) : IMoneyViewFactory
{
    public async Task<MoneyView> CurrentAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        var chosen = await db.AppSettings
            .OrderBy(x => x.Id)
            .Select(x => x.DisplayCurrency)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(chosen) || chosen == Money.BaseCurrency)
            return MoneyView.Base(fx, today);

        // Prove today's rate once. If the sources are down, the app reports PLN rather than
        // guessing — every later conversion in this request leans on this number.
        var probe = await fx.ConvertFromBaseAsync(1m, chosen, today, ct);
        if (!probe.IsSuccess || probe.Value!.Rate <= 0)
            return MoneyView.Base(fx, today);

        return MoneyView.For(chosen, fx, probe.Value.Rate, today);
    }
}
