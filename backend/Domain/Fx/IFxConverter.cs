namespace FinanceApp.Domain.Fx;

/// Порт для конвертації валют. Реалізація у фазі 1 — лише PLN (PlnOnlyFxConverter);
/// у M2 замінюється на NBP + ECB без зміни цього інтерфейсу.
public interface IFxConverter
{
    /// Конвертує <paramref name="amount"/> у валюті <paramref name="currency"/> в базову (PLN)
    /// за курсом, чинним на <paramref name="date"/>.
    Task<FxConversion> ConvertToBaseAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default);
}
