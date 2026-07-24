namespace FinanceApp.Domain.Fx;

/// Result of converting an amount into the base currency (PLN).
/// Rate and EffectiveDate are stored on the transaction — the rate is fixed to that date.
public record FxConversion(decimal AmountBase, decimal Rate, DateOnly RateDate);
