using FinanceApp.Domain;
using FinanceApp.Domain.Fx;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure.Fx;

/// Конвертує в базову валюту (PLN): PLN → 1:1; інакше бере курс із кешу, а якщо нема —
/// по черзі питає провайдерів (NBP → ECB) і кешує результат. Курс і дата фіксуються
/// на транзакції — заднім числом не перераховуємо.
public sealed class CachingFxConverter(AppDbContext db, IEnumerable<IFxRateProvider> providers) : IFxConverter
{
    private readonly IReadOnlyList<IFxRateProvider> _providers = providers.ToList();

    public async Task<FxConversion> ConvertToBaseAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default)
    {
        currency = currency.ToUpperInvariant();
        if (currency == Money.BaseCurrency)
            return new FxConversion(Round(amount), 1m, date);

        var (rate, effectiveDate) = await GetRateAsync(currency, date, ct);
        return new FxConversion(Round(amount * rate), rate, effectiveDate);
    }

    private async Task<(decimal rate, DateOnly effectiveDate)> GetRateAsync(
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
            catch { quote = null; } // мережевий збій одного джерела не валить запис — пробуємо наступне

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

        throw new NotSupportedException(
            $"Не вдалося отримати курс {currency}→PLN на {date:yyyy-MM-dd} (джерела недоступні або валюта не підтримується).");
    }

    // Гроші округлюємо до 2 знаків, half-up (AwayFromZero) — як у касовому чеку.
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
