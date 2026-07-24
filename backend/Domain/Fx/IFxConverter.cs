using FinanceApp.Domain.Common;

namespace FinanceApp.Domain.Fx;

/// Port for currency conversion. Phase 1 implementation — NBP + ECB (CachingFxConverter).
public interface IFxConverter
{
    /// Converts <paramref name="amount"/> in <paramref name="currency"/> to base (PLN)
    /// using the rate effective on <paramref name="date"/>. Returns Fail(Unsupported) if no rate.
    Task<Result<FxConversion>> ConvertToBaseAsync(
        decimal amount, string currency, DateOnly date, CancellationToken ct = default);
}
