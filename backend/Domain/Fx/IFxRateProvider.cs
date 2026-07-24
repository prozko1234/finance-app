namespace FinanceApp.Domain.Fx;

/// Rate for a date: how many PLN per 1 unit of currency (mid rate), and which date it is
/// effective for (may be earlier than requested — weekends/holidays).
public record FxQuote(decimal PlnPerUnit, DateOnly EffectiveDate);

/// External rate source (NBP, ECB...). Returns null when no rate is available
/// (currency not covered by the source, or data unavailable).
public interface IFxRateProvider
{
    string Name { get; }
    Task<FxQuote?> GetPlnPerUnitAsync(string currency, DateOnly date, CancellationToken ct = default);
}
