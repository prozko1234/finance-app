namespace FinanceApp.Domain.Fx;

/// Курс на дату: скільки PLN коштує 1 одиниця валюти (mid rate), і за яку дату він чинний
/// (може бути раніше запитаної — вихідні/свята).
public record FxQuote(decimal PlnPerUnit, DateOnly EffectiveDate);

/// Зовнішнє джерело курсів (NBP, ECB...). Повертає null, якщо курсу нема
/// (валюта не підтримується джерелом або дані недоступні).
public interface IFxRateProvider
{
    string Name { get; }
    Task<FxQuote?> GetPlnPerUnitAsync(string currency, DateOnly date, CancellationToken ct = default);
}
