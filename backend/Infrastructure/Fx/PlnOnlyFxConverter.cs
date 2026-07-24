using FinanceApp.Domain;
using FinanceApp.Domain.Fx;

namespace FinanceApp.Infrastructure.Fx;

/// Фаза 1: підтримується лише базова валюта (PLN). Реальна конвертація через
/// NBP + ECB з'явиться в M2 і замінить цю реалізацію без зміни IFxConverter.
public sealed class PlnOnlyFxConverter : IFxConverter
{
    public Task<FxConversion> ConvertToBaseAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default)
    {
        if (!string.Equals(currency, Money.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"Валюта '{currency}' ще не підтримується (буде в M2). Поки що використовуй {Money.BaseCurrency}.");

        return Task.FromResult(new FxConversion(amount, 1m, date));
    }
}
