using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Fx;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure.Fx;

/// Converts to base currency (PLN): PLN → 1:1; otherwise takes the rate from cache, and if
/// missing, asks providers in order (NBP → ECB) and caches the result. Rate and date are
/// fixed on the transaction — never recomputed retroactively.
public sealed class CachingFxConverter(AppDbContext db, IEnumerable<IFxRateProvider> providers) : IFxConverter
{
    private readonly IReadOnlyList<IFxRateProvider> _providers = providers.ToList();

    public async Task<Result<FxConversion>> ConvertToBaseAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default)
    {
        currency = currency.ToUpperInvariant();
        if (currency == Money.BaseCurrency)
            return Result<FxConversion>.Ok(new FxConversion(Round(amount), 1m, date));

        var rate = await GetRateAsync(currency, date, ct);
        if (rate is null)
            return Error.Unsupported(
                $"Не вдалося отримати курс {currency}→PLN на {date:yyyy-MM-dd} (джерела недоступні або валюта не підтримується).");

        var (plnPerUnit, effectiveDate) = rate.Value;
        return Result<FxConversion>.Ok(new FxConversion(Round(amount * plnPerUnit), plnPerUnit, effectiveDate));
    }

    public async Task<Result<FxConversion>> ConvertFromBaseAsync(
        decimal baseAmount, string currency, DateOnly date, CancellationToken ct = default)
    {
        currency = currency.ToUpperInvariant();
        if (currency == Money.BaseCurrency)
            return Result<FxConversion>.Ok(new FxConversion(Round(baseAmount), 1m, date));

        var rate = await GetRateAsync(currency, date, ct);
        if (rate is null)
            return Error.Unsupported(
                $"Не вдалося отримати курс {currency}→PLN на {date:yyyy-MM-dd} (джерела недоступні або валюта не підтримується).");

        var (plnPerUnit, effectiveDate) = rate.Value;
        return Result<FxConversion>.Ok(new FxConversion(Round(baseAmount / plnPerUnit), plnPerUnit, effectiveDate));
    }

    private async Task<(decimal rate, DateOnly effectiveDate)?> GetRateAsync(
        string currency, DateOnly date, CancellationToken ct)
    {
        var cached = await db.FxRates
            .FirstOrDefaultAsync(r => r.Currency == currency && r.Date == date, ct);
        if (cached is not null)
            return (cached.PlnPerUnit, cached.EffectiveDate);

        foreach (var provider in _providers)
        {
            FxQuote? quote;
            try { quote = await provider.GetPlnPerUnitAsync(currency, date, ct); }
            catch { quote = null; } // one source failing must not break the entry — try the next

            if (quote is { PlnPerUnit: > 0 })
            {
                db.FxRates.Add(new FxRate
                {
                    Currency = currency,
                    Date = date,
                    PlnPerUnit = quote.PlnPerUnit,
                    EffectiveDate = quote.EffectiveDate,
                    Source = provider.Name,
                    FetchedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(ct);
                return (quote.PlnPerUnit, quote.EffectiveDate);
            }
        }

        return null;
    }

    // Round money to 2 decimals, half-up (AwayFromZero) — like a cash receipt.
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
