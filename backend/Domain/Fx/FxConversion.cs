namespace FinanceApp.Domain.Fx;

/// Результат конвертації суми в базову валюту (PLN).
/// Rate і RateDate зберігаються на транзакції — курс фіксується на дату.
public record FxConversion(decimal AmountBase, decimal Rate, DateOnly RateDate);
